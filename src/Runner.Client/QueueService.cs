using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GitHub.DistributedTask.WebApi;
using System.Threading;
using GitHub.Runner.Common;
using GitHub.Services.Common;
using GitHub.DistributedTask.Pipelines;

namespace Runner.Client
{
    partial class Program
    {
        private class QueueService : Runner.Server.IQueueService, IRunnerServer
        {
            private string customConfigDir;

            private SemaphoreSlim semaphore;

            public QueueService(string customConfigDir, int parallel)
            {
                this.customConfigDir = customConfigDir;
                semaphore = new SemaphoreSlim(parallel, parallel);
            }

            public Task<TaskAgent> AddAgentAsync(int agentPoolId, TaskAgent agent)
            {
                throw new NotImplementedException();
            }

            public Task ConnectAsync(Uri serverUrl, VssCredentials credentials)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgentSession> CreateAgentSessionAsync(int poolId, TaskAgentSession session, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task DeleteAgentAsync(int agentPoolId, ulong agentId)
            {
                throw new NotImplementedException();
            }

            public Task DeleteAgentAsync(ulong agentId)
            {
                throw new NotImplementedException();
            }

            public Task DeleteAgentMessageAsync(int poolId, long messageId, Guid sessionId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task DeleteAgentSessionAsync(int poolId, Guid sessionId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgentJobRequest> FinishAgentRequestAsync(int poolId, long requestId, Guid lockToken, DateTime finishTime, TaskResult result, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgentMessage> GetAgentMessageAsync(int poolId, Guid sessionId, long? lastMessageId, TaskAgentStatus status, string runnerVersion, string os, string architecture, bool disableUpdate, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<List<TaskAgentPool>> GetAgentPoolsAsync(string agentPoolName = null, TaskAgentPoolType poolType = TaskAgentPoolType.Automation)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgentJobRequest> GetAgentRequestAsync(int poolId, long requestId, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<List<TaskAgent>> GetAgentsAsync(int agentPoolId, string agentName = null)
            {
                throw new NotImplementedException();
            }

            public Task<List<TaskAgent>> GetAgentsAsync(string agentName)
            {
                throw new NotImplementedException();
            }

            public Task<PackageMetadata> GetPackageAsync(string packageType, string platform, string version, bool includeToken, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public Task<List<PackageMetadata>> GetPackagesAsync(string packageType, string platform, int top, bool includeToken, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public void Initialize(IHostContext context)
            {
                throw new NotImplementedException();
            }

            public async void PickJob(AgentJobRequestMessage message, CancellationToken token, string[] labels)
            {
                semaphore.Wait();
                try {
                    var agentname = Path.GetRandomFileName();
                    string tmpdir = Path.Combine(Path.GetFullPath(customConfigDir), agentname);
                    Directory.CreateDirectory(tmpdir);
                    File.WriteAllText(Path.Join(tmpdir, ".runner"), "{\"isHostedServer\": false, \"agentName\": \"my-runner\", \"workFolder\": \"_work\"}");
                    var ctx = new HostContext("RUNNERCLIENT", customConfigDir: tmpdir);
                    ctx.PutService<IRunnerServer>(this);
                    ctx.PutService<ITerminal>(new Terminal());
                    ctx.PutServiceFactory<IProcessInvoker, WrapProcService>();
                    var dispatcher = new GitHub.Runner.Listener.JobDispatcher();
                    dispatcher.Initialize(ctx);
                    dispatcher.Run(message, true);
                    await dispatcher.WaitAsync(token);
                    await dispatcher.ShutdownAsync();
                    try {
                        Directory.Delete(tmpdir, true);
                    } catch {

                    }
                } finally {
                    semaphore.Release();
                }
            }

            public Task RefreshConnectionAsync(RunnerConnectionType connectionType, TimeSpan timeout)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgentJobRequest> RenewAgentRequestAsync(int poolId, long requestId, Guid lockToken, string orchestrationId, CancellationToken cancellationToken)
            {
                return Task.FromResult(new TaskAgentJobRequest());
            }

            public Task<TaskAgent> ReplaceAgentAsync(int agentPoolId, TaskAgent agent)
            {
                throw new NotImplementedException();
            }

            public void SetConnectionTimeout(RunnerConnectionType connectionType, TimeSpan timeout)
            {
                throw new NotImplementedException();
            }

            public Task<TaskAgent> UpdateAgentUpdateStateAsync(int agentPoolId, ulong agentId, string currentState, string trace)
            {
                throw new NotImplementedException();
            }
        }
    }
}
