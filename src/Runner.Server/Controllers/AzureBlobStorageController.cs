using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.IO;
using Microsoft.AspNetCore.Http.Extensions;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;

namespace Runner.Server.Controllers
{

    [ApiController]
    [Route("_apis/v1/blob")]
    public class AzureBlobStorageController : ControllerBase {

        private static string GenerateQuery(string storagePath, string contentType, string contentDisposition, string contentEncoding, bool write = false)
        {
            return $"storagePath={Uri.EscapeDataString(storagePath)}&contentType={Uri.EscapeDataString(contentType ?? "")}&contentDisposition={Uri.EscapeDataString(contentDisposition ?? "")}&contentEncoding={Uri.EscapeDataString(contentEncoding ?? "")}";
        }

        private static string CreateSignature(string query) {
            using var rsa = RSA.Create(Startup.AccessTokenParameter);
            return Base64UrlEncoder.Encode(rsa.SignData(Encoding.UTF8.GetBytes(query), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        private static bool VerifySignature(string sig, string query) {
            using var rsa = RSA.Create(Startup.AccessTokenParameter);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(query), Base64UrlEncoder.DecodeBytes(sig), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public static string CreateSignedUrl(string serverUrl, string storagePath, string contentType = null, string contentDisposition = null, string contentEncoding = null, bool write = false)
        {
            var query = GenerateQuery(storagePath, contentType ?? "", contentDisposition ?? "", contentEncoding ?? "", write);
            return new Uri(new Uri(serverUrl), $"_apis/v1/blob?sig={CreateSignature(query)}&{query}").ToString();
        }

        [HttpPut]
        [AllowAnonymous]
        public async Task<IActionResult> Upload(string storagePath, string contentType, string contentDisposition, string contentEncoding, string sig, string comp = null, bool seal = false, string blockid = null) {
            if(string.IsNullOrEmpty(sig) || !VerifySignature(sig, GenerateQuery(storagePath, contentType, contentDisposition, contentEncoding, true))) {
                return NotFound();
            }
            var _targetFilePath = Path.Combine(GitHub.Runner.Sdk.GharunUtil.GetLocalStorage());
            Directory.CreateDirectory(Path.Combine(_targetFilePath, Path.GetDirectoryName(storagePath)));
            if(comp == "block" || comp == "appendBlock" || comp == null) {
                using(var targetStream = new FileStream(Path.Combine(_targetFilePath, string.IsNullOrWhiteSpace(blockid) ? storagePath : $"{storagePath}-{System.Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(blockid))}"), FileMode.OpenOrCreate | FileMode.Append, FileAccess.Write, FileShare.Write)) {
                    await Request.Body.CopyToAsync(targetStream);
                }
                return Created(HttpContext.Request.GetEncodedUrl(), null);
            }
            if(comp == "blocklist") {
                XElement blockList = await XElement.LoadAsync(Request.Body, LoadOptions.None, Request.HttpContext.RequestAborted);
                using(var targetStream = new FileStream(Path.Combine(_targetFilePath, storagePath), FileMode.Create, FileAccess.Write, FileShare.Write))
                foreach(var block in from item in blockList.Descendants("Latest") select item.Value) {
                    var filename = Path.Combine(_targetFilePath, $"{storagePath}-{Convert.ToBase64String(Encoding.UTF8.GetBytes(block))}");
                    using(var sourceStream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        await sourceStream.CopyToAsync(targetStream);
                    }
                    System.IO.File.Delete(filename);
                }
                return Created(HttpContext.Request.GetEncodedUrl(), null);
            }
            return Ok();
        }

        private static IEnumerable<string> GetBlobFilePaths(string storagePath)
        {
            var _targetFilePath = Path.Combine(GitHub.Runner.Sdk.GharunUtil.GetLocalStorage(), storagePath);
            if(System.IO.File.Exists(_targetFilePath))
            {
                yield return _targetFilePath;
            }
            var _targetRoot = Path.GetFileName(_targetFilePath);
            foreach(var block in Directory.EnumerateFiles(Path.GetDirectoryName(_targetFilePath)))
            {
                var bfn = Path.GetFileName(block);
                if(bfn.StartsWith(_targetRoot + "-"))
                {
                    yield return block;
                }
            }
        }

        public static string GetBlobFilePath(string storagePath)
        {
            return GetBlobFilePaths(storagePath).FirstOrDefault();
        }

        public static void DeleteBlobFilePath(string storagePath)
        {
            foreach(var block in GetBlobFilePaths(storagePath))
            {
                System.IO.File.Delete(block);
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Download(string storagePath, string contentType, string contentDisposition, string contentEncoding, string sig) {
            if(string.IsNullOrEmpty(sig) || !VerifySignature(sig, GenerateQuery(storagePath, contentType, contentDisposition, contentEncoding, false))) {
                return NotFound();
            }
            var _targetFilePath = GetBlobFilePath(storagePath);
            if(string.IsNullOrWhiteSpace(_targetFilePath)) {
                return NotFound();
            }
            if(contentDisposition != null) {
                Response.Headers.ContentDisposition = contentDisposition;
            }
            if(contentEncoding != null) {
                Response.Headers.ContentEncoding = contentEncoding;
            }
            return new FileStreamResult(System.IO.File.OpenRead(_targetFilePath), contentType ?? "application/octet-stream") { EnableRangeProcessing = true };
        }
    }
}
