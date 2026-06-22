
using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Runner.Server.Models;

namespace Runner.Server.Controllers {

    [ApiController]
    [Route("_apis/artifactcachemanagement")]
    public class ArtifactCacheManagementController : VssControllerBase{
        private SqLiteDb db;

        public ArtifactCacheManagementController(IConfiguration conf, SqLiteDb db) : base(conf) {
            this.db = db;
        }

        [HttpDelete("artifacts")]
        public void DeleteArtifacts() {
            var records = db.ArtifactRecords.ToArray();
            db.ArtifactRecords.RemoveRange(records);
            db.SaveChanges();
            foreach(var rec in records) {
                AzureBlobStorageController.DeleteBlobFilePath("artifacts/" + rec.StoreName);
            }
        }

        [HttpDelete("cache")]
        public void DeleteCache() {
            var caches = db.Caches.ToArray();
            db.Caches.RemoveRange(caches);
            db.SaveChanges();
            foreach(var cache in caches) {
                AzureBlobStorageController.DeleteBlobFilePath("cache/" + cache.Storage);
            }
        }
    }
}
