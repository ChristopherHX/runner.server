using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using GitHub.DistributedTask.WebApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Runner.Server.Models
{
    public class WorkflowRun {

        public int Id { get; set; }
        public Workflow Workflow { get; set; }
        public List<WorkflowRunAttempt> Attempts { get; set; }

    }
}