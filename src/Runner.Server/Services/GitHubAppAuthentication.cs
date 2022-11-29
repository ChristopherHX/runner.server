using System;
using System.Threading.Tasks;

namespace Runner.Server.Services {
    public class GitHubAppAuthentication : IAsyncDisposable {
        private string GitServerUrl { get; }
        public string Token { get; private set; }

        public GitHubAppAuthentication() {

        }

        public GitHubAppAuthentication(string gitServerUrl, string token) {
            GitServerUrl = gitServerUrl;
            Token = token;
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);

            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if(!string.IsNullOrEmpty(GitServerUrl) && !string.IsNullOrEmpty(Token)) {
                var appClient2 = new Octokit.GitHubClient(new Octokit.ProductHeaderValue("gharun"), new Uri(GitServerUrl))
                {
                    Credentials = new Octokit.Credentials(token)
                };
                await appClient2.Connection.Delete(new Uri("installation/token", UriKind.Relative));

                Token = null;
            }
        }
    }
}