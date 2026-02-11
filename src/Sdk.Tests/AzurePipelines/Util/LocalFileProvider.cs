namespace Runner.Server.Azure.Devops
{
    public class LocalFileProvider : IFileProvider
    {
        private IDictionary<string, string> repos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public LocalFileProvider(string cwd)
        {
            repos["self"] = cwd;
        }

        public async Task<string> ReadFile(string repositoryAndRef, string path)
        {
            var filePath = ResolveFilePath(repositoryAndRef, path);
            return await Task.FromResult(System.IO.File.ReadAllText(filePath));
        }

        #region Helper methods for local repositories
        public void AddRepo(string repositoryAndRef, string folderPath)
        {
            if (!folderPath.EndsWith(Path.DirectorySeparatorChar))
            {
                folderPath = $"{folderPath}{Path.DirectorySeparatorChar}";
            }
            repos[repositoryAndRef] = folderPath;
        }

        private string ResolveFilePath(string repositoryAndRef, string path)
        {
            var cwd = GetRepoLocalFolder(repositoryAndRef);
            if (path.StartsWith("/"))
            {
                path = path.Substring(1);
            }
            return Path.Combine(cwd, path);
        }

        private string GetRepoLocalFolder(string repositoryAndRef)
        {
            return repos[repositoryAndRef ?? "self"];
        }
        #endregion
    }
}
