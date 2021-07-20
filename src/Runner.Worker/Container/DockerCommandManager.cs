using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;

namespace GitHub.Runner.Worker.Container
{
    [ServiceLocator(Default = typeof(DockerCommandManager))]
    public interface IDockerCommandManager : IRunnerService
    {
        string DockerPath { get; }
        string DockerInstanceLabel { get; }
        string Os { get; }
        string Arch { get; }
        Version ClientVersion { get; }
        Version ServerVersion { get; }
        Task<DockerVersion> DockerVersion(IExecutionContext context);
        Task<int> DockerPull(IExecutionContext context, string image);
        Task<int> DockerPull(IExecutionContext context, string image, string configFileDirectory);
        Task<int> DockerBuild(IExecutionContext context, string workingDirectory, string dockerFile, string dockerContext, string tag);
        Task<string> DockerCreate(IExecutionContext context, ContainerInfo container);
        Task<int> DockerRun(IExecutionContext context, ContainerInfo container, EventHandler<ProcessDataReceivedEventArgs> stdoutDataReceived, EventHandler<ProcessDataReceivedEventArgs> stderrDataReceived);
        Task<int> DockerStart(IExecutionContext context, string containerId);
        Task<int> DockerLogs(IExecutionContext context, string containerId);
        Task<List<string>> DockerPS(IExecutionContext context, string options);
        Task<int> DockerRemove(IExecutionContext context, string containerId);
        Task<int> DockerNetworkCreate(IExecutionContext context, string network);
        Task<int> DockerNetworkRemove(IExecutionContext context, string network);
        Task<int> DockerNetworkPrune(IExecutionContext context);
        Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command);
        Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command, List<string> outputs);
        Task<List<string>> DockerInspect(IExecutionContext context, string dockerObject, string options);
        Task<List<PortMapping>> DockerPort(IExecutionContext context, string containerId);
        Task<int> DockerLogin(IExecutionContext context, string configFileDirectory, string registry, string username, string password);
    }

    public class DockerCommandManager : RunnerService, IDockerCommandManager
    {
        public string DockerPath { get; private set; }

        public string DockerInstanceLabel { get; private set; }

        public string Os { get {
            if(os != null) {
                return os;
            }
            throw new Exception("Unsupported");
        } }

        public string Arch { get {
            if(arch != null) {
                return arch;
            }
            throw new Exception("Unsupported");
        } }

        public Version ClientVersion { get; private set; }
        public Version ServerVersion { get; private set; }

        private string os = null;
        private string arch = null;

        public override void Initialize(IHostContext hostContext)
        {
            base.Initialize(hostContext);
            DockerPath = WhichUtil.Which("docker", true, Trace);
            DockerInstanceLabel = IOUtil.GetSha256Hash(hostContext.GetDirectory(WellKnownDirectory.ConfigRoot)).Substring(0, 6);
        }

        public async Task<DockerVersion> DockerVersion(IExecutionContext context)
        {
            string serverVersionStr = (await ExecuteDockerCommandAsync(context, "version", "--format '{{.Server.APIVersion}}'")).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverVersionStr, "Docker.Server.Version");
            context.Output($"Docker daemon API version: {serverVersionStr}");

            string clientVersionStr = (await ExecuteDockerCommandAsync(context, "version", "--format '{{.Client.APIVersion}}'")).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverVersionStr, "Docker.Client.Version");
            context.Output($"Docker client API version: {clientVersionStr}");

            string serverOsStr = (await ExecuteDockerCommandAsync(context, "version", "--format '{{.Server.Os}}'")).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverOsStr, "Docker.Server.Os");
            context.Output($"Docker server Os: {serverOsStr}");
            Regex osarchRegex = new Regex("^[\"'](.+)[\"']$", RegexOptions.IgnoreCase);
            var osMatchResult = osarchRegex.Match(serverOsStr);
            if(osMatchResult.Success) {
                os = osMatchResult.Groups[1].Value;
            }

            string serverArchStr = (await ExecuteDockerCommandAsync(context, "version", "--format '{{.Server.Arch}}'")).FirstOrDefault();
            ArgUtil.NotNullOrEmpty(serverArchStr, "Docker.Server.Arch");
            context.Output($"Docker server Arch: {serverArchStr}");
            
            var archMatchResult = osarchRegex.Match(serverArchStr);
            if(archMatchResult.Success) {
                arch = archMatchResult.Groups[1].Value;
            }

            // we interested about major.minor.patch version
            Regex verRegex = new Regex("\\d+\\.\\d+(\\.\\d+)?", RegexOptions.IgnoreCase);

            Version serverVersion = null;
            var serverVersionMatchResult = verRegex.Match(serverVersionStr);
            if (serverVersionMatchResult.Success && !string.IsNullOrEmpty(serverVersionMatchResult.Value))
            {
                if (!Version.TryParse(serverVersionMatchResult.Value, out serverVersion))
                {
                    serverVersion = null;
                }
                ServerVersion = serverVersion;
            }

            Version clientVersion = null;
            var clientVersionMatchResult = verRegex.Match(serverVersionStr);
            if (clientVersionMatchResult.Success && !string.IsNullOrEmpty(clientVersionMatchResult.Value))
            {
                if (!Version.TryParse(clientVersionMatchResult.Value, out clientVersion))
                {
                    clientVersion = null;
                }
                ClientVersion = clientVersion;
            }

            return new DockerVersion(serverVersion, clientVersion);
        }

        public Task<int> DockerPull(IExecutionContext context, string image)
        {
            return DockerPull(context, image, null);
        }

        public async Task<int> DockerPull(IExecutionContext context, string image, string configFileDirectory)
        {
            string extraopts = "";
            if(ClientVersion >= new Version(1, 32) && ServerVersion >= new Version(1, 32))
            {
                var val = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_ARCH");
                if(val?.Length > 0) {
                    if(val.Contains(' ') || val.Contains('\t')) {
                        context.Warning("Ignored docker platform `{val}`, because it contains one or more spaces");
                    } else {
                        extraopts = "--platform " + val;
                    }
                }
            }
            if (string.IsNullOrEmpty(configFileDirectory))
            {
                return await ExecuteDockerCommandAsync(context, $"pull{(extraopts.Length > 0 ? " " + extraopts : "")}", image, context.CancellationToken);
            }
            return await ExecuteDockerCommandAsync(context, $"--config {configFileDirectory} pull{(extraopts.Length > 0 ? " " + extraopts : "")}", image, context.CancellationToken);
        }

        public async Task<int> DockerBuild(IExecutionContext context, string workingDirectory, string dockerFile, string dockerContext, string tag)
        {
            string extraopts = "";
            if(ClientVersion >= new Version(1, 38) && ServerVersion >= new Version(1, 38)) {
                var val = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_ARCH");
                if(val?.Length > 0) {
                    if(val.Contains(' ') || val.Contains('\t')) {
                        context.Warning("Ignored docker platform `{val}`, because it contains one or more spaces");
                    } else {
                        extraopts = "--platform " + val;
                    }
                }
            }
            return await ExecuteDockerCommandAsync(context, "build", $"-t {tag}{(extraopts.Length > 0 ? " " + extraopts : "")} -f \"{dockerFile}\" \"{dockerContext}\"", workingDirectory, context.CancellationToken);
        }

        public async Task<string> DockerCreate(IExecutionContext context, ContainerInfo container)
        {
            IList<string> dockerOptions = new List<string>();
            if(ClientVersion >= new Version(1, 32) && ServerVersion >= new Version(1, 32)) {
                var val = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_ARCH");
                if(val?.Length > 0) {
                    if(val.Contains(' ') || val.Contains('\t')) {
                        context.Warning("Ignored docker platform `{val}`, because it contains one or more spaces");
                    } else {
                        dockerOptions.Add("--platform " + val);
                    }
                }
            }
            var privileged = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_PRIVILEGED")?.ToLower();
            if(privileged == "true" || privileged == "1") {
                dockerOptions.Add($"--privileged");
            }
            var userns = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_USERNS");
            if(userns != null) {
                if(userns.Contains(' ')) {
                    context.Warning("Ignored docker userns `{userns}`, because it contains one or more spaces");
                } else {
                    dockerOptions.Add($"--userns {userns}");
                }
            }
            // OPTIONS
            dockerOptions.Add($"--name {container.ContainerDisplayName}");
            dockerOptions.Add($"--label {DockerInstanceLabel}");
            if (!string.IsNullOrEmpty(container.ContainerWorkDirectory))
            {
                dockerOptions.Add($"--workdir {container.ContainerWorkDirectory}");
            }
            if (!string.IsNullOrEmpty(container.ContainerNetwork))
            {
                dockerOptions.Add($"--network {container.ContainerNetwork}");
            }
            if (!string.IsNullOrEmpty(container.ContainerNetworkAlias))
            {
                dockerOptions.Add($"--network-alias {container.ContainerNetworkAlias}");
            }
            foreach (var port in container.UserPortMappings)
            {
                dockerOptions.Add($"-p {port.Value}");
            }
            dockerOptions.Add($"{container.ContainerCreateOptions}");
            foreach (var env in container.ContainerEnvironmentVariables)
            {
                if (String.IsNullOrEmpty(env.Value))
                {
                    dockerOptions.Add($"-e \"{env.Key}\"");
                }
                else
                {
                    dockerOptions.Add($"-e \"{env.Key}={env.Value.Replace("\"", "\\\"")}\"");
                }
            }

            // Watermark for GitHub Action environment
            dockerOptions.Add("-e GITHUB_ACTIONS=true");

            // Set CI=true when no one else already set it.
            // CI=true is common set in most CI provider in GitHub
            if (!container.ContainerEnvironmentVariables.ContainsKey("CI"))
            {
                dockerOptions.Add("-e CI=true");
            }

            foreach (var volume in container.MountVolumes)
            {
                // replace `"` with `\"` and add `"{0}"` to all path.
                String volumeArg;
                if (String.IsNullOrEmpty(volume.SourceVolumePath))
                {
                    // Anonymous docker volume
                    volumeArg = $"-v \"{volume.TargetVolumePath.Replace("\"", "\\\"")}\"";
                }
                else
                {
                    // Named Docker volume / host bind mount
                    volumeArg = $"-v \"{volume.SourceVolumePath.Replace("\"", "\\\"")}\":\"{volume.TargetVolumePath.Replace("\"", "\\\"")}\"";
                }
                if (volume.ReadOnly)
                {
                    volumeArg += ":ro";
                }
                dockerOptions.Add(volumeArg);
            }
            if (!string.IsNullOrEmpty(container.ContainerEntryPoint))
            {
                dockerOptions.Add($"--entrypoint \"{container.ContainerEntryPoint}\"");
            }
            // IMAGE
            dockerOptions.Add($"{container.ContainerImage}");

            // COMMAND
            // Intentionally blank. Always overwrite ENTRYPOINT and/or send ARGs

            // [ARG...]
            dockerOptions.Add($"{container.ContainerEntryPointArgs}");

            var optionsString = string.Join(" ", dockerOptions);
            List<string> outputStrings = await ExecuteDockerCommandAsync(context, "create", optionsString);

            return outputStrings.FirstOrDefault();
        }

        public async Task<int> DockerRun(IExecutionContext context, ContainerInfo container, EventHandler<ProcessDataReceivedEventArgs> stdoutDataReceived, EventHandler<ProcessDataReceivedEventArgs> stderrDataReceived)
        {
            IList<string> dockerOptions = new List<string>();
            if(ClientVersion >= new Version(1, 32) && ServerVersion >= new Version(1, 32)) {
                var val = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_ARCH");
                if(val?.Length > 0) {
                    if(val.Contains(' ') || val.Contains('\t')) {
                        context.Warning("Ignored docker platform `{val}`, because it contains one or more spaces");
                    } else {
                        dockerOptions.Add("--platform " + val);
                    }
                }
            }
            var privileged = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_PRIVILEGED")?.ToLower();
            if(privileged == "true" || privileged == "1") {
                dockerOptions.Add($"--privileged");
            }
            var userns = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_USERNS");
            if(userns != null) {
                if(userns.Contains(' ')) {
                    context.Warning("Ignored docker userns `{userns}`, because it contains one or more spaces");
                } else {
                    dockerOptions.Add($"--userns {userns}");
                }
            }
            // OPTIONS
            dockerOptions.Add($"--name {container.ContainerDisplayName}");
            dockerOptions.Add($"--label {DockerInstanceLabel}");

            dockerOptions.Add($"--workdir {container.ContainerWorkDirectory}");

            var keepContainer = System.Environment.GetEnvironmentVariable("RUNNER_CONTAINER_KEEP");
            if(keepContainer == null || keepContainer != "true" && keepContainer != "1") {
                dockerOptions.Add($"--rm");
            }

            foreach (var env in container.ContainerEnvironmentVariables)
            {
                // e.g. -e MY_SECRET maps the value into the exec'ed process without exposing
                // the value directly in the command
                dockerOptions.Add($"-e {env.Key}");
            }

            // Watermark for GitHub Action environment
            dockerOptions.Add("-e GITHUB_ACTIONS=true");

            // Set CI=true when no one else already set it.
            // CI=true is common set in most CI provider in GitHub
            if (!container.ContainerEnvironmentVariables.ContainsKey("CI"))
            {
                dockerOptions.Add("-e CI=true");
            }

            if (!string.IsNullOrEmpty(container.ContainerEntryPoint))
            {
                dockerOptions.Add($"--entrypoint \"{container.ContainerEntryPoint}\"");
            }

            if (!string.IsNullOrEmpty(container.ContainerNetwork))
            {
                dockerOptions.Add($"--network {container.ContainerNetwork}");
            }

            foreach (var volume in container.MountVolumes)
            {
                // replace `"` with `\"` and add `"{0}"` to all path.
                String volumeArg;
                if (String.IsNullOrEmpty(volume.SourceVolumePath))
                {
                    // Anonymous docker volume
                    volumeArg = $"-v \"{volume.TargetVolumePath.Replace("\"", "\\\"")}\"";
                }
                else
                {
                    // Named Docker volume / host bind mount
                    volumeArg = $"-v \"{volume.SourceVolumePath.Replace("\"", "\\\"")}\":\"{volume.TargetVolumePath.Replace("\"", "\\\"")}\"";
                }
                if (volume.ReadOnly)
                {
                    volumeArg += ":ro";
                }
                dockerOptions.Add(volumeArg);
            }
            // IMAGE
            dockerOptions.Add($"{container.ContainerImage}");

            // COMMAND
            // Intentionally blank. Always overwrite ENTRYPOINT and/or send ARGs

            // [ARG...]
            dockerOptions.Add($"{container.ContainerEntryPointArgs}");

            var optionsString = string.Join(" ", dockerOptions);
            return await ExecuteDockerCommandAsync(context, "run", optionsString, container.ContainerEnvironmentVariables, stdoutDataReceived, stderrDataReceived, context.CancellationToken);
        }

        public async Task<int> DockerStart(IExecutionContext context, string containerId)
        {
            return await ExecuteDockerCommandAsync(context, "start", containerId, context.CancellationToken);
        }

        public async Task<int> DockerRemove(IExecutionContext context, string containerId)
        {
            return await ExecuteDockerCommandAsync(context, "rm", $"--force {containerId}", context.CancellationToken);
        }

        public async Task<int> DockerLogs(IExecutionContext context, string containerId)
        {
            return await ExecuteDockerCommandAsync(context, "logs", $"--details {containerId}", context.CancellationToken);
        }

        public async Task<List<string>> DockerPS(IExecutionContext context, string options)
        {
            return await ExecuteDockerCommandAsync(context, "ps", options);
        }

        public async Task<int> DockerNetworkCreate(IExecutionContext context, string network)
        {
            if(os == "windows") return await ExecuteDockerCommandAsync(context, "network", $"create --label {DockerInstanceLabel} {network} --driver nat", context.CancellationToken);
            return await ExecuteDockerCommandAsync(context, "network", $"create --label {DockerInstanceLabel} {network}", context.CancellationToken);
        }

        public async Task<int> DockerNetworkRemove(IExecutionContext context, string network)
        {
            return await ExecuteDockerCommandAsync(context, "network", $"rm {network}", context.CancellationToken);
        }

        public async Task<int> DockerNetworkPrune(IExecutionContext context)
        {
            return await ExecuteDockerCommandAsync(context, "network", $"prune --force --filter \"label={DockerInstanceLabel}\"", context.CancellationToken);
        }

        public async Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command)
        {
            return await ExecuteDockerCommandAsync(context, "exec", $"{options} {containerId} {command}", context.CancellationToken);
        }

        public async Task<int> DockerExec(IExecutionContext context, string containerId, string options, string command, List<string> output)
        {
            ArgUtil.NotNull(output, nameof(output));

            string arg = $"exec {options} {containerId} {command}".Trim();
            context.Command($"{DockerPath} {arg}");

            object outputLock = new object();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        output.Add(message.Data);
                    }
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    lock (outputLock)
                    {
                        output.Add(message.Data);
                    }
                }
            };

            return await processInvoker.ExecuteAsync(
                            workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Work),
                            fileName: DockerPath,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: false,
                            outputEncoding: null,
                            cancellationToken: CancellationToken.None);
        }

        public async Task<List<string>> DockerInspect(IExecutionContext context, string dockerObject, string options)
        {
            return await ExecuteDockerCommandAsync(context, "inspect", $"{options} {dockerObject}");
        }

        public async Task<List<PortMapping>> DockerPort(IExecutionContext context, string containerId)
        {
            List<string> portMappingLines = await ExecuteDockerCommandAsync(context, "port", containerId);
            return DockerUtil.ParseDockerPort(portMappingLines);
        }

        public Task<int> DockerLogin(IExecutionContext context, string configFileDirectory, string registry, string username, string password)
        {
            string args = $"--config {configFileDirectory} login {registry} -u {username} --password-stdin";
            context.Command($"{DockerPath} {args}");

            var input = Channel.CreateBounded<string>(new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = true });
            input.Writer.TryWrite(password);

            var processInvoker = HostContext.CreateService<IProcessInvoker>();

            return processInvoker.ExecuteAsync(
                workingDirectory: context.GetGitHubContext("workspace"),
                fileName: DockerPath,
                arguments: args,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                killProcessOnCancel: false,
                redirectStandardIn: input,
                cancellationToken: context.CancellationToken);
        }

        private Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteDockerCommandAsync(context, command, options, null, cancellationToken);
        }

        private async Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, IDictionary<string, string> environment, EventHandler<ProcessDataReceivedEventArgs> stdoutDataReceived, EventHandler<ProcessDataReceivedEventArgs> stderrDataReceived, CancellationToken cancellationToken = default(CancellationToken))
        {
            string arg = $"{command} {options}".Trim();
            context.Command($"{DockerPath} {arg}");

            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += stdoutDataReceived;
            processInvoker.ErrorDataReceived += stderrDataReceived;


            return await processInvoker.ExecuteAsync(
                workingDirectory: context.GetGitHubContext("workspace"),
                fileName: DockerPath,
                arguments: arg,
                environment: environment,
                requireExitCodeZero: false,
                outputEncoding: null,
                killProcessOnCancel: false,
                cancellationToken: cancellationToken);
        }

        private async Task<int> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options, string workingDirectory, CancellationToken cancellationToken = default(CancellationToken))
        {
            string arg = $"{command} {options}".Trim();
            context.Command($"{DockerPath} {arg}");

            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                context.Output(message.Data);
            };

            return await processInvoker.ExecuteAsync(
                workingDirectory: workingDirectory ?? context.GetGitHubContext("workspace"),
                fileName: DockerPath,
                arguments: arg,
                environment: null,
                requireExitCodeZero: false,
                outputEncoding: null,
                killProcessOnCancel: false,
                redirectStandardIn: null,
                cancellationToken: cancellationToken);
        }

        private async Task<List<string>> ExecuteDockerCommandAsync(IExecutionContext context, string command, string options)
        {
            string arg = $"{command} {options}".Trim();
            context.Command($"{DockerPath} {arg}");

            List<string> output = new List<string>();
            var processInvoker = HostContext.CreateService<IProcessInvoker>();
            processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    output.Add(message.Data);
                    context.Output(message.Data);
                }
            };

            processInvoker.ErrorDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
            {
                if (!string.IsNullOrEmpty(message.Data))
                {
                    context.Output(message.Data);
                }
            };

            await processInvoker.ExecuteAsync(
                            workingDirectory: context.GetGitHubContext("workspace"),
                            fileName: DockerPath,
                            arguments: arg,
                            environment: null,
                            requireExitCodeZero: true,
                            outputEncoding: null,
                            cancellationToken: CancellationToken.None);

            return output;
        }

        // private async Task<List<string>> ExecuteDockerCommandAsync(string command, string options)
        // {
        //     string arg = $"{command} {options}".Trim();

        //     List<string> output = new List<string>();
        //     var processInvoker = new ProcessInvoker(null);
        //     processInvoker.OutputDataReceived += delegate (object sender, ProcessDataReceivedEventArgs message)
        //     {
        //         if (!string.IsNullOrEmpty(message.Data))
        //         {
        //             output.Add(message.Data);
        //         }
        //     };

        //     await processInvoker.ExecuteAsync(
        //                     workingDirectory: "",
        //                     fileName: DockerPath,
        //                     arguments: arg,
        //                     environment: null,
        //                     requireExitCodeZero: true,
        //                     outputEncoding: null,
        //                     cancellationToken: CancellationToken.None);

        //     return output;
        // }
    }
}
