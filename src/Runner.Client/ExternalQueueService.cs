using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GitHub.DistributedTask.WebApi;
using System.Threading;
using GitHub.Runner.Common;
using GitHub.Services.Common;
using GitHub.DistributedTask.Pipelines;
using Newtonsoft.Json;
using System.Text.Unicode;
using GitHub.Services.WebApi;

namespace Runner.Client
{
    partial class Program
    {
        private class ExternalQueueService : Runner.Server.IQueueService, IRunnerServer
        {
            private string customConfigDir;
            private Parameters parameters;
            private SemaphoreSlim semaphore;

            public ExternalQueueService(Parameters parameters)
            {
                this.parameters = parameters;
                semaphore = new SemaphoreSlim(parameters.Parallel ?? 1, parameters.Parallel ?? 1);
            }

            public string Prefix { get; private set; }
            public string Suffix { get; private set; }

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
                await semaphore.WaitAsync();
                try {

                    // CreateExternalRunnerDirectory(parameters, out _, out var prefix, out var suffix, out _, out var tmpdir);
                    // this.Prefix = prefix;
                    // this.Suffix = suffix;
                    // File.WriteAllText(Path.Join(tmpdir, ".agent"), "{\"isHostedServer\": false, \"agentName\": \"my-runner\", \"workFolder\": \"_work\"}");
                    // File.WriteAllText(Path.Join(tmpdir, ".runner"), "{\"isHostedServer\": false, \"agentName\": \"my-runner\", \"workFolder\": \"_work\"}");

                    var proc = new System.Diagnostics.Process();
                    proc.StartInfo.FileName = Environment.ProcessPath;
                    // proc.StartInfo.ArgumentList.AddRange(["spawn", "/Users/christopher/Downloads/gh-act-runner/github-act-runner"]);
                    // proc.StartInfo.ArgumentList.Add("worker");

                    // proc.StartInfo.FileName = "podman";
                    // proc.StartInfo.ArgumentList.AddRange(["spawn", "podman", "run", "--privileged", "-v", "docker-run:/run/docker/", "-v", "docker-data:/var/lib/docker/", "--rm", "-i", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/.runner:/runnertmp/.runner", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/github-acrions-runner.py:/bin/github-acrions-runner.py", "--entrypoint=python3", "actions-runner-linux-x64-2.321.0:latest", "/bin/github-acrions-runner.py", "/runnertmp/bin/Runner.Worker"]);
                    //proc.StartInfo.ArgumentList.Add("--trace");
                    // proc.StartInfo.ArgumentList.AddRange(["run", "--privileged", "--rm", "-i", "-v", "/Users/christopher/Downloads/github-act-runner-linux-amd64:/github-act-runner", "--entrypoint=/github-act-runner", "actions-runner-linux-x64-2.321.0:latest", "worker"]);
                    proc.StartInfo.ArgumentList.AddRange(["spawn", "podman", "run", "--privileged", "--rm", "-i", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/.runner:/runnertmp/.runner", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/github-acrions-runner.py:/bin/github-acrions-runner.py", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/startup.sh:/usr/local/bin/startup.sh", "-v", "/Users/christopher/Documents/ActionsAndPipelines/runner.server/docker-runner/bin/entrypoint-dind.sh:/usr/local/bin/entrypoint-dind.sh", "--entrypoint=/usr/local/bin/entrypoint-dind.sh", "summerwind/actions-runner-dind:v2.321.0-ubuntu-22.04"]);
                    
                    proc.StartInfo.RedirectStandardError = true;
                    proc.StartInfo.RedirectStandardInput = true;
                    proc.StartInfo.RedirectStandardOutput = true;
                    // proc.StartInfo.StandardInputEncoding = System.Text.Encoding
                    // proc.StartInfo.WorkingDirectory = tmpdir;
                    proc.Start();
                    var stdout = proc.StandardOutput.BaseStream.CopyToAsync(System.Console.OpenStandardOutput());
                    var stderr = proc.StandardError.BaseStream.CopyToAsync(System.Console.OpenStandardError());
                    var pstdin = proc.StandardInput.BaseStream;
                    byte[] intHolder = new byte[4];
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(intHolder, 1);
                    await pstdin.WriteAsync(intHolder);
                    await pstdin.FlushAsync();
                    var data = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToString(message));
                    System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(intHolder, (uint)data.Length);
                    await pstdin.WriteAsync(intHolder);
                    await pstdin.FlushAsync();
                    await pstdin.WriteAsync(data);
                    await pstdin.FlushAsync();
                    var registration = token.Register(() => {
                        byte[] intHolder = new byte[4];
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(intHolder, 2);
                        pstdin.Write(intHolder);
                        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(intHolder, 0);
                        pstdin.Write(intHolder);
                        pstdin.Flush();
                    });
                    await Task.WhenAll(stdout, stderr);
                    await proc.WaitForExitAsync();
                    registration.Unregister();

                    // CreateExternalRunnerDirectory(parameters, out _, out var prefix, out var suffix, out _, out var tmpdir);
                    // this.Prefix = prefix;
                    // this.Suffix = suffix;
                    // File.WriteAllText(Path.Join(tmpdir, ".agent"), "{\"isHostedServer\": false, \"agentName\": \"my-runner\", \"workFolder\": \"_work\"}");
                    // File.WriteAllText(Path.Join(tmpdir, ".runner"), "{\"isHostedServer\": false, \"agentName\": \"my-runner\", \"workFolder\": \"_work\"}");
                    // var ctx = new HostContext("EXTERNALRUNNERCLIENT", customConfigDir: tmpdir);
                    // ctx.PutService<IRunnerServer>(this);
                    // ctx.PutService<ExternalQueueService>(this);
                    // ctx.PutService<ITerminal>(new Terminal());
                    // ctx.PutServiceFactory<IProcessInvoker, WrapProcService>();
                    // var dispatcher = new GitHub.Runner.Listener.JobDispatcher();
                    // dispatcher.Initialize(ctx);
                    // dispatcher.Run(message, true);
                    // await dispatcher.WaitAsync(token);
                    // await dispatcher.ShutdownAsync();
                    // if(!parameters.KeepRunnerDirectory) {
                    //     try {
                    //         Directory.Delete(tmpdir, true);
                    //     } catch {

                    //     }
                    // }
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
