using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace Runner.Server.Services {
    public class GitHubApp {
        private string GitServerUrl { get; }
        private string GitHubAppPrivateKeyFile { get; }
        private int GitHubAppId { get; }

        public GitHubApp(IConfiguration conf) {
            GitServerUrl = configuration.GetSection("Runner.Server")?.GetValue<string>("GitServerUrl") ?? "";
            GitHubAppPrivateKeyFile = configuration.GetSection("Runner.Server")?.GetValue<string>("GitHubAppPrivateKeyFile") ?? "";
            GitHubAppId = configuration.GetSection("Runner.Server")?.GetValue<int>("GitHubAppId") ?? 0;
        }

        public GitHubAppAuthentication Authenticate(string repositoryName, Dictionary<string, string> permissions = null) {
            if(!string.IsNullOrEmpty(GitHubAppPrivateKeyFile) && GitHubAppId != 0) {
                try {
                    var ownerAndRepo = repository_name.Split("/", 2);
                    // Use GitHubJwt library to create the GitHubApp Jwt Token using our private certificate PEM file
                    var generator = new GitHubJwt.GitHubJwtFactory(
                        new GitHubJwt.FilePrivateKeySource(GitHubAppPrivateKeyFile),
                        new GitHubJwt.GitHubJwtFactoryOptions
                        {
                            AppIntegrationId = GitHubAppId, // The GitHub App Id
                            ExpirationSeconds = 500 // 10 minutes is the maximum time allowed
                        }
                    );
                    var jwtToken = generator.CreateEncodedJwtToken();
                    // Pass the JWT as a Bearer token to Octokit.net
                    var appClient = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("gharun"), new Uri(GitServerUrl))
                    {
                        Credentials = new Octokit.Credentials(jwtToken, Octokit.AuthenticationType.Bearer)
                    };
                    var installation = await appClient.GitHubApps.GetRepositoryInstallationForCurrent(ownerAndRepo[0], ownerAndRepo[1]);
                    if(permissions == null) {
                        permissions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        permissions["metadata"] = "read";
                        permissions["contents"] = "read";
                    }
                    var response = await appClient.Connection.Post<Octokit.AccessToken>(Octokit.ApiUrls.AccessTokens(installation.Id), new { Permissions = permissions }, Octokit.AcceptHeaders.GitHubAppsPreview, Octokit.AcceptHeaders.GitHubAppsPreview);
                    return new GitHubAppAuthentication(GitServerUrl, response.Body.Token);
                } catch {

                }
            }
            return null;
        }
    }
}