﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitHub.Services.Location;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Runner.Server.Controllers
{
    [ApiController]
    [Route("{owner}/{repo}/_apis/[controller]")]
    public class ConnectionDataController : VssControllerBase
    {

        [HttpGet]
        [Authorize(AuthenticationSchemes = "Bearer")]
        public async Task<IActionResult> Get()
        {
            return await Ok(new ConnectionData() {
                InstanceId = Guid.NewGuid(),
                LocationServiceData = new LocationServiceData() {
                    ServiceDefinitions = new ServiceDefinition[] {
                        new ServiceDefinition("AgentPools", Guid.Parse("a8c47e17-4d56-4a56-92bb-de7ea7dc65be"), "AgentPools", "/_apis/v1/AgentPools", RelativeToSetting.Context, "AgentPools", "AgentPools") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("Agent", Guid.Parse("e298ef32-5878-4cab-993c-043836571f42"), "Agent", "/_apis/v1/Agent/{poolId}/{agentId}", RelativeToSetting.Context, "Agent", "Agent") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("AgentSession", Guid.Parse("134e239e-2df3-4794-a6f6-24f1f19ec8dc"), "AgentSession", "/_apis/v1/AgentSession/{poolId}/{sessionId}", RelativeToSetting.Context, "AgentSession", "AgentSession") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("Message", Guid.Parse("c3a054f6-7a8a-49c0-944e-3a8e5d7adfd7"), "Message", "/_apis/v1/Message/{poolId}/{messageId}", RelativeToSetting.Context, "Message", "Message") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("AgentRequest", Guid.Parse("fc825784-c92a-4299-9221-998a02d1b54f"), "AgentRequest", "/_apis/v1/AgentRequest/{poolId}/{requestId}", RelativeToSetting.Context, "AgentRequest", "AgentRequest") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("ActionDownloadInfo", Guid.Parse("27d7f831-88c1-4719-8ca1-6a061dad90eb"), "ActionDownloadInfo", "/_apis/v1/ActionDownloadInfo/{scopeIdentifier}/{hubName}/{planId}", RelativeToSetting.Context, "ActionDownloadInfo", "ActionDownloadInfo") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("TimeLineWebConsoleLog", Guid.Parse("858983e4-19bd-4c5e-864c-507b59b58b12"), "TimeLineWebConsoleLog", "/_apis/v1/TimeLineWebConsoleLog/{scopeIdentifier}/{hubName}/{planId}/{timelineId}/{recordId}", RelativeToSetting.Context, "TimeLineWebConsoleLog", "TimeLineWebConsoleLog") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("Timeline", Guid.Parse("8893bc5b-35b2-4be7-83cb-99e683551db4"), "Timeline", "/_apis/v1/Timeline/{scopeIdentifier}/{hubName}/{planId}/{timelineId}", RelativeToSetting.Context, "Timeline", "Timeline") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("Logfiles", Guid.Parse("46f5667d-263a-4684-91b1-dff7fdcf64e2"), "Logfiles", "/_apis/v1/Logfiles/{scopeIdentifier}/{hubName}/{planId}/{logId}", RelativeToSetting.Context, "Logfiles", "Logfiles") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        new ServiceDefinition("FinishJob", Guid.Parse("557624af-b29e-4c20-8ab0-0399d2204f3f"), "FinishJob", "/_apis/v1/FinishJob/{scopeIdentifier}/{hubName}/{planId}", RelativeToSetting.Context, "FinishJob", "FinishJob") { ResourceVersion = 6, MinVersion = new Version(1, 0), MaxVersion = new Version(12, 0), RelativeToSetting = RelativeToSetting.FullyQualified },
                        // ToDo add "83597576-cc2c-453c-bea6-2882ae6a1653"
                    }
                }
            });
        }
    }
}
