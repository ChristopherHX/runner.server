using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using System.IO;
using Runner.Server.Models;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using GitHub.Actions.Pipelines.WebApi;
using System.Net.Mime;
using System.Security.Cryptography;
using Swashbuckle.AspNetCore.Annotations;

namespace Runner.Server.Controllers
{

    [ApiController]
    [Route("twirp/github.actions.results.api.v1.ArtifactService")]
    [Authorize(AuthenticationSchemes = "Bearer", Policy = "AgentJob")]
    public class ArtifactControllerV2 : VssControllerBase{
        private readonly SqLiteDb _context;
        public ArtifactControllerV2(SqLiteDb _context, IConfiguration configuration) : base(configuration)
        {
            this._context = _context;
        }

        [HttpPost("CreateArtifact")]
        public async Task<Github.Actions.Results.Api.V1.CreateArtifactResponse> CreateArtifact([FromBody, Protobuf] Github.Actions.Results.Api.V1.CreateArtifactRequest body) {
            var guid = Guid.Parse(body.WorkflowJobRunBackendId);
            var jobInfo = (from j in _context.Jobs where j.JobId == guid select new { j.runid, j.WorkflowRunAttempt.Attempt }).FirstOrDefault();
            var artifacts = new ArtifactController(_context, Configuration);
            var fname = $"{body.Name}.zip";
            if(!string.IsNullOrWhiteSpace(body.MimeType) && (body.MimeType != "application/zip" || body.Name.ToLower().EndsWith(".zip")))
            {
                fname = body.Name;
            }
            var container = await artifacts.CreateContainer(jobInfo.runid, jobInfo.Attempt, new CreateActionsStorageArtifactParameters() { Name = body.Name }, jobInfo.Attempt);
            if(_context.Entry(container).Collection(c => c.Files).Query().Any()) {
                // Duplicated Artifact of the same name in the same Attempt => fail
                return new Github.Actions.Results.Api.V1.CreateArtifactResponse() {
                    Ok = false
                };
            }
            var record = new ArtifactRecord() {FileName = fname, StoreName = Path.GetRandomFileName(), GZip = false, FileContainer = container, ContentType = body.MimeType} ;
            _context.ArtifactRecords.Add(record);
            await _context.SaveChangesAsync();

            return new Github.Actions.Results.Api.V1.CreateArtifactResponse
            {
                Ok = true,
                SignedUploadUrl = AzureBlobStorageController.CreateSignedUrl(ServerUrl, "artifacts/" + record.StoreName, write: true)
            };
        }

        [HttpPost("FinalizeArtifact")]
        public Github.Actions.Results.Api.V1.FinalizeArtifactResponse FinalizeArtifact([FromBody, Protobuf] Github.Actions.Results.Api.V1.FinalizeArtifactRequest body) {
            var attempt = long.Parse(User.FindFirst("attempt")?.Value ?? "-1");
            var artifactsMinAttempt = long.Parse(User.FindFirst("artifactsMinAttempt")?.Value ?? "-1");
            var runid = long.Parse(body.WorkflowRunBackendId);

            var container = (from fileContainer in _context.ArtifactFileContainer where (fileContainer.Container.Attempt.Attempt >= artifactsMinAttempt || artifactsMinAttempt == -1) && (fileContainer.Container.Attempt.Attempt <= attempt || attempt == -1) && fileContainer.Container.Attempt.WorkflowRun.Id == runid && fileContainer.Files.Count == 1 && body.Name.ToLower() == fileContainer.Name.ToLower() orderby fileContainer.Container.Attempt.Attempt descending select new{ fileContainer, fileContainer.Files.First().StoreName }).First();
            container.fileContainer.Size = body.Size;
            var hash = body.Hash?.ToLower() ?? "";
            if(hash.StartsWith("sha256:"))
            {
                using(var fs = new FileStream(AzureBlobStorageController.GetBlobFilePath("artifacts/" + container.StoreName), FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    var expectedHash = "sha256:" + Convert.ToHexString(SHA256.HashData(fs)).ToLower();
                    if (hash != expectedHash) {
                        return new Github.Actions.Results.Api.V1.FinalizeArtifactResponse
                        {
                            Ok = false,
                            ArtifactId = container.fileContainer.Id
                        };
                    }
                }
            }
            return new Github.Actions.Results.Api.V1.FinalizeArtifactResponse
            {
                Ok = true,
                ArtifactId = container.fileContainer.Id
            };
        }

        [HttpPost("ListArtifacts")]
        public Github.Actions.Results.Api.V1.ListArtifactsResponse ListArtifacts([FromBody, Protobuf] Github.Actions.Results.Api.V1.ListArtifactsRequest body) {
            var resp = new Github.Actions.Results.Api.V1.ListArtifactsResponse();

            var attempt = long.Parse(User.FindFirst("attempt")?.Value ?? "-1");
            var artifactsMinAttempt = long.Parse(User.FindFirst("artifactsMinAttempt")?.Value ?? "-1");
            var runid = long.Parse(body.WorkflowRunBackendId);
            resp.Artifacts.AddRange(from fileContainer in _context.ArtifactFileContainer where (fileContainer.Container.Attempt.Attempt >= artifactsMinAttempt || artifactsMinAttempt == -1) && (fileContainer.Container.Attempt.Attempt <= attempt || attempt == -1) && fileContainer.Container.Attempt.WorkflowRun.Id == runid && fileContainer.Files.Count == 1 && !fileContainer.Files.FirstOrDefault().FileName.Contains('/') && (fileContainer.Files.FirstOrDefault().FileName.EndsWith(".zip") || !string.IsNullOrWhiteSpace(fileContainer.Files.FirstOrDefault().ContentType)) && (body.IdFilter == null || body.IdFilter == fileContainer.Id) && (body.NameFilter == null || body.NameFilter.ToLower() == fileContainer.Name.ToLower()) orderby fileContainer.Container.Attempt.Attempt descending select new Github.Actions.Results.Api.V1.ListArtifactsResponse_MonolithArtifact
            {
                CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(System.DateTimeOffset.UtcNow),
                DatabaseId = fileContainer.Id,
                Name = fileContainer.Name,
                Size = fileContainer.Size ?? 0,
                WorkflowRunBackendId = body.WorkflowRunBackendId,
                WorkflowJobRunBackendId = body.WorkflowJobRunBackendId
            });
            return resp;
        }

        [HttpPost("GetSignedArtifactURL")]
        public async Task<Github.Actions.Results.Api.V1.GetSignedArtifactURLResponse> GetSignedArtifactURL([FromBody, Protobuf] Github.Actions.Results.Api.V1.GetSignedArtifactURLRequest body) {
            var attempt = long.Parse(User.FindFirst("attempt")?.Value ?? "-1");
            var artifactsMinAttempt = long.Parse(User.FindFirst("artifactsMinAttempt")?.Value ?? "-1");
            var runid = long.Parse(body.WorkflowRunBackendId);
            var file = await (from fileContainer in _context.ArtifactFileContainer where (fileContainer.Container.Attempt.Attempt >= artifactsMinAttempt || artifactsMinAttempt == -1) && (fileContainer.Container.Attempt.Attempt <= attempt || attempt == -1) && fileContainer.Container.Attempt.WorkflowRun.Id == runid && fileContainer.Files.Count == 1 && (fileContainer.Files.FirstOrDefault().FileName.ToLower() == $"{body.Name}.zip".ToLower() || fileContainer.Files.FirstOrDefault().ContentType != null) orderby fileContainer.Container.Attempt.Attempt descending select fileContainer.Files.FirstOrDefault()).FirstAsync();
            return new Github.Actions.Results.Api.V1.GetSignedArtifactURLResponse
            {
                SignedUrl = AzureBlobStorageController.CreateSignedUrl(ServerUrl, "artifacts/" + file.StoreName, contentType: string.IsNullOrWhiteSpace(file.ContentType) ? "application/zip" : file.ContentType, contentDisposition: new ContentDisposition(DispositionTypeNames.Inline) { FileName = file.FileName }.ToString())
            };
        }

        [AllowAnonymous]
        [HttpPost("DeleteArtifact")]
        [SwaggerResponse(200, type: typeof(Github.Actions.Results.Api.V1.DeleteArtifactResponse))]
        public async Task<IActionResult> DeleteArtifact([FromBody, Protobuf] Github.Actions.Results.Api.V1.DeleteArtifactRequest body) {
            var attempt = long.Parse(User.FindFirst("attempt")?.Value ?? "-1");
            var artifactsMinAttempt = long.Parse(User.FindFirst("artifactsMinAttempt")?.Value ?? "-1");
            var runid = long.Parse(body.WorkflowRunBackendId);
            var res = await (from fileContainer in _context.ArtifactFileContainer where (fileContainer.Container.Attempt.Attempt >= artifactsMinAttempt || artifactsMinAttempt == -1) && (fileContainer.Container.Attempt.Attempt <= attempt || attempt == -1) && fileContainer.Container.Attempt.WorkflowRun.Id == runid && fileContainer.Files.Count == 1 && fileContainer.Files.FirstOrDefault().FileName.ToLower() == $"{body.Name}.zip".ToLower() orderby fileContainer.Container.Attempt.Attempt descending select new { fileContainer, file = fileContainer.Files.FirstOrDefault(), fileContainer.Container }).FirstAsync();
            if(res.file == null || res.fileContainer == null || res.Container == null) {
                return NotFound();
            }
            _context.ArtifactRecords.Remove(res.file);
            _context.ArtifactFileContainer.Remove(res.fileContainer);
            AzureBlobStorageController.DeleteBlobFilePath("artifacts/" + res.file.StoreName);
            await _context.SaveChangesAsync();

            var resp = new Github.Actions.Results.Api.V1.DeleteArtifactResponse {
                Ok = true,
                ArtifactId = res?.fileContainer?.Id ?? 0
            };
            return new OkObjectResult(resp);
        }
    }
}
