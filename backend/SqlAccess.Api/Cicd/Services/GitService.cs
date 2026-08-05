using LibGit2Sharp;
using LibGit2Sharp.Handlers;

namespace SqlAccess.Api.Cicd.Services;

public interface IGitService
{
    /// <summary>Clones (or updates) the repo/branch into a per-website working dir. Returns (repoPath, commitSha, commitMessage).</summary>
    Task<(string repoPath, string commitSha, string commitMessage)> PrepareAsync(
        int websiteId, string repoUrl, string branch, string? pat, Func<string, Task> log, CancellationToken ct);
}

public sealed class GitService : IGitService
{
    private readonly string _reposRoot;

    public GitService(IHostEnvironment env)
    {
        _reposRoot = Path.Combine(env.ContentRootPath, "App_Data", "cicd", "repos");
        Directory.CreateDirectory(_reposRoot);
    }

    public async Task<(string, string, string)> PrepareAsync(
        int websiteId, string repoUrl, string branch, string? pat, Func<string, Task> log, CancellationToken ct)
    {
        var path = Path.Combine(_reposRoot, websiteId.ToString());

        CredentialsHandler? creds = string.IsNullOrWhiteSpace(pat)
            ? null
            : (_, _, _) => new UsernamePasswordCredentials { Username = pat, Password = string.Empty };

        return await Task.Run(() =>
        {
            var fresh = !Repository.IsValid(path);
            if (Directory.Exists(path) && fresh)
                DeleteDir(path);

            if (fresh)
            {
                log($"Cloning {repoUrl} ({branch})...").GetAwaiter().GetResult();
                Repository.Clone(repoUrl, path, new CloneOptions
                {
                    BranchName = branch,
                    FetchOptions = { CredentialsProvider = creds },
                });
            }
            else
            {
                log($"Repository exists — fetching latest for {branch}...").GetAwaiter().GetResult();
                using var repo = new Repository(path);
                var remote = repo.Network.Remotes["origin"];
                var specs = remote.FetchRefSpecs.Select(s => s.Specification);
                Commands.Fetch(repo, remote.Name, specs, new FetchOptions { CredentialsProvider = creds }, null);

                var remoteBranch = repo.Branches[$"origin/{branch}"]
                    ?? throw new InvalidOperationException($"Branch '{branch}' not found on origin.");

                var local = repo.Branches[branch];
                if (local is null)
                    local = repo.CreateBranch(branch, remoteBranch.Tip);
                repo.Branches.Update(local, b => { b.TrackedBranch = remoteBranch.CanonicalName; });

                Commands.Checkout(repo, local);
                repo.Reset(ResetMode.Hard, remoteBranch.Tip);
            }

            using var r = new Repository(path);
            var tip = r.Head.Tip;
            log($"Checked out {tip.Sha[..7]} — {tip.MessageShort}").GetAwaiter().GetResult();
            return (path, tip.Sha, tip.MessageShort ?? "");
        }, ct);
    }

    private static void DeleteDir(string path)
    {
        // .git files are read-only; clear the attribute before deleting.
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(path, recursive: true);
    }
}
