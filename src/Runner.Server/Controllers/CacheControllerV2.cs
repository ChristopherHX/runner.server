using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Google.Protobuf;
using System;
using System.Threading.Tasks;
using System.IO;
using Runner.Server.Models;
using System.Linq;

namespace Runner.Server.Controllers
{

    [ApiController]
    [Route("twirp/github.actions.results.api.v1.CacheService")]
    [Authorize(AuthenticationSchemes = "Bearer", Policy = "AgentJob")]
    public class CacheControllerV2 : VssControllerBase{
        private readonly SqLiteDb _context;
        private readonly JsonFormatter formatter;

        public CacheControllerV2(SqLiteDb _context, IConfiguration configuration) : base(configuration)
        {
            this._context = _context;
            formatter = new JsonFormatter(JsonFormatter.Settings.Default.WithIndentation().WithPreserveProtoFieldNames(true).WithFormatDefaultValues(false));
        }

        [HttpPost("CreateCacheEntry")]
        public async Task<string> CreateCacheEntry([FromBody, Protobuf] Github.Actions.Results.Api.V1.CreateCacheEntryRequest body) {
            var filename = Path.GetRandomFileName();
            var reference = User.FindFirst("ref")?.Value ?? "refs/heads/main";
            var repository = User.FindFirst("repository")?.Value ?? "Unknown/Unknown";
            var record = new CacheRecord() { Key = body.Key, LastUpdated = DateTime.Now, Ref = reference, Version = body.Version, Storage = filename, Repo = repository };
            _context.Caches.Add(record);
            await _context.SaveChangesAsync();
            var resp = new Github.Actions.Results.Api.V1.CreateCacheEntryResponse
            {
                Ok = true,
                SignedUploadUrl = AzureBlobStorageController.CreateSignedUrl(ServerUrl, "cache/" + filename, write: true)
            };
            return formatter.Format(resp);
        }

        [HttpPost("FinalizeCacheEntryUpload")]
        public string FinalizeCacheEntryUpload([FromBody, Protobuf] Github.Actions.Results.Api.V1.FinalizeCacheEntryUploadRequest body) {
            var record = _context.Caches.First(c => c.Key == body.Key && c.Version == body.Version);
            var resp = new Github.Actions.Results.Api.V1.FinalizeCacheEntryUploadResponse
            {
                Ok = true,
                EntryId = record.Id
            };
            return formatter.Format(resp);
        }

        [HttpPost("GetCacheEntryDownloadURL")]
        public string GetCacheEntryDownloadURL([FromBody, Protobuf] Github.Actions.Results.Api.V1.GetCacheEntryDownloadURLRequest body) {
            var a = body.RestoreKeys.Prepend(body.Key).ToArray();
            var version = body.Version;
            var defaultRef = User.FindFirst("defaultRef")?.Value ?? "refs/heads/main";
            var reference = User.FindFirst("ref")?.Value ?? "refs/heads/main";
            var repository = User.FindFirst("repository")?.Value ?? "Unknown/Unknown";
            foreach(var cref in reference != defaultRef ? new [] { reference, defaultRef } : new [] { reference }) {
                foreach (var item in a) {
                    var record = (from rec in _context.Caches where rec.Repo.ToLower() == repository.ToLower() && rec.Ref == cref && rec.Key.ToLower() == item.ToLower() && (rec.Version == null || rec.Version == "" || rec.Version == version) orderby rec.LastUpdated descending select rec).FirstOrDefault()
                        ?? (from rec in _context.Caches where rec.Repo.ToLower() == repository.ToLower() && rec.Ref == cref && rec.Key.ToLower().StartsWith(item.ToLower()) && (rec.Version == null || rec.Version == "" || rec.Version == version) orderby rec.LastUpdated descending select rec).FirstOrDefault();
                    if(record != null) {
                        var resp = new Github.Actions.Results.Api.V1.GetCacheEntryDownloadURLResponse
                        {
                            Ok = true,
                            MatchedKey = record.Key,
                            SignedDownloadUrl = AzureBlobStorageController.CreateSignedUrl(ServerUrl, "cache/" + record.Storage)
                        };
                        return formatter.Format(resp);
                    }
                }
            }
            return formatter.Format(new Github.Actions.Results.Api.V1.GetCacheEntryDownloadURLResponse { Ok = false });
        }
    }
}
