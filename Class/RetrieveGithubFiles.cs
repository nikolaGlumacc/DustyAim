using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Class
{
    internal sealed class RetrieveGithubFiles
    {
        public static string RepoOwner = "Babyhamsta";
        public static string RepoName = "Aimmy";
        public static string RepoBranch = "Aimmy-V1";

        public static async Task<IEnumerable<DownloadItem>> ListContents(string RepoPath)
        {
            return await ListContents(RepoOwner, RepoName, RepoPath, RepoBranch);
        }

        public static async Task<IEnumerable<DownloadItem>> ListContents(string owner, string repo, string repoPath, string branch)
        {
            var client = new GitHubClient(new ProductHeaderValue("Github-API-Test"));
            IReadOnlyList<RepositoryContent> contents;

            try
            {
                if (string.IsNullOrWhiteSpace(branch))
                {
                    contents = await client.Repository.Content.GetAllContents(owner, repo, repoPath);
                }
                else
                {
                    contents = await client.Repository.Content.GetAllContentsByRef(owner, repo, repoPath, branch);
                }
            }
            catch
            {
                return Array.Empty<DownloadItem>();
            }

            string branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;

            return contents.Select(content => new DownloadItem(content.Name, owner, repo, branchName, repoPath));
        }
    }

    public sealed record DownloadItem(string Name, string Owner, string Repo, string Branch, string Path)
    {
        public string DisplayTitle => string.IsNullOrWhiteSpace(Owner) ? Name : $"{Name} ({Owner}/{Repo})";
        public string SourceKey => $"{Owner}|{Repo}|{Branch}|{Path}|{Name}";
        public string RemotePath => Path;
    }
}
