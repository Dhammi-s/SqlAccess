using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Cicd.Providers;
using SqlAccess.Api.Data;
using SqlAccess.Api.Services;

namespace SqlAccess.Api.Cicd.Services;

public interface IDeploymentOrchestrator
{
    Task RunAsync(int deploymentId, CancellationToken ct);
}

/// <summary>Runs the end-to-end pipeline for one deployment: checkout → build → publish → upload.</summary>
public sealed class DeploymentOrchestrator : IDeploymentOrchestrator
{
    private readonly AppDbContext _db;
    private readonly ILogService _logs;
    private readonly IGitService _git;
    private readonly IBuildService _build;
    private readonly IEncryptionService _enc;
    private readonly IDeploymentQueue _queue;
    private readonly IEnumerable<IDeploymentProvider> _providers;
    private readonly IGitHubActionsService _actions;

    public DeploymentOrchestrator(
        AppDbContext db, ILogService logs, IGitService git, IBuildService build,
        IEncryptionService enc, IDeploymentQueue queue, IEnumerable<IDeploymentProvider> providers,
        IGitHubActionsService actions)
    {
        _db = db;
        _logs = logs;
        _git = git;
        _build = build;
        _enc = enc;
        _queue = queue;
        _providers = providers;
        _actions = actions;
    }

    public async Task RunAsync(int deploymentId, CancellationToken ct)
    {
        var deployment = await _db.Deployments.FirstOrDefaultAsync(d => d.DeploymentId == deploymentId, ct);
        if (deployment is null) return;
        var website = await _db.Websites.FirstOrDefaultAsync(w => w.WebsiteId == deployment.WebsiteId, ct);
        if (website is null) return;

        Func<string, string, Task> log = (type, msg) => _logs.LogAsync(deploymentId, type, msg, ct);
        Func<int, string?, Task> progress = (pct, label) => _logs.ProgressAsync(deploymentId, pct, label, ct);

        void CheckCancel()
        {
            if (_queue.IsCancelRequested(deploymentId)) throw new OperationCanceledException();
        }

        try
        {
            await _logs.StatusAsync(deploymentId, DeploymentStatus.Running, ct);
            await log("Info", $"=== Deployment #{deploymentId} started for '{website.WebsiteName}' ({deployment.Branch}) ===");

            var pat = _enc.Decrypt(website.GitPat);

            // GitHub Actions mode: trigger the workflow and track it (works on shared hosting).
            if (!string.IsNullOrWhiteSpace(website.WorkflowFile))
            {
                await RunViaGitHubActions(deployment, website, pat, log, progress, CheckCancel, ct);
                await log("Info", "=== Deployment completed successfully ===");
                await _logs.StatusAsync(deploymentId, DeploymentStatus.Success, ct);
                return;
            }

            // 1. Checkout
            CheckCancel();
            var (repoPath, sha, message) = await _git.PrepareAsync(
                website.WebsiteId, website.RepositoryUrl ?? "", deployment.Branch ?? website.DefaultBranch ?? "main",
                pat, m => log("Info", m), ct);

            deployment.CommitId = sha;
            deployment.CommitMessage = message;
            await _db.SaveChangesAsync(ct);

            // 2. Build command(s)
            CheckCancel();
            if (!string.IsNullOrWhiteSpace(website.BuildCommand))
            {
                await log("Info", $"$ {website.BuildCommand}");
                var code = await _build.RunAsync(repoPath, website.BuildCommand!, log, ct);
                if (code != 0) throw new InvalidOperationException($"Build command exited with code {code}.");
                await log("Info", "Build step completed.");
            }

            // 3. Publish command
            CheckCancel();
            if (!string.IsNullOrWhiteSpace(website.PublishCommand))
            {
                await log("Info", $"$ {website.PublishCommand}");
                var code = await _build.RunAsync(repoPath, website.PublishCommand!, log, ct);
                if (code != 0) throw new InvalidOperationException($"Publish command exited with code {code}.");
                await log("Info", "Publish step completed.");
            }

            // 4. Locate the publish folder
            CheckCancel();
            var publishPath = ResolvePublishFolder(repoPath, website.PublishFolder);
            if (publishPath is null)
                throw new InvalidOperationException(
                    $"Publish folder not found. Looked for '{website.PublishFolder}' and common fallbacks under {repoPath}.");
            await log("Info", $"Publish folder: {publishPath}");

            // 5. Upload via the website's configured provider (FTP / SFTP / …)
            CheckCancel();
            var providerKey = string.IsNullOrWhiteSpace(website.DeployProvider) ? "FTP" : website.DeployProvider;
            var provider = _providers.FirstOrDefault(p => p.Key == providerKey)
                ?? throw new InvalidOperationException($"No '{providerKey}' deployment provider registered.");
            await log("Info", $"Uploading via {providerKey}...");

            var context = new DeployContext(
                LocalFolder: publishPath,
                FtpHost: website.FtpHost,
                FtpPort: website.FtpPort,
                FtpUsername: website.FtpUsername,
                FtpPassword: _enc.Decrypt(website.FtpPassword),
                FtpRootFolder: website.FtpRootFolder);

            await provider.DeployAsync(context, log, progress, ct);

            await log("Info", "=== Deployment completed successfully ===");
            await _logs.StatusAsync(deploymentId, DeploymentStatus.Success, ct);
        }
        catch (OperationCanceledException)
        {
            await log("Warning", "Deployment cancelled.");
            await _logs.StatusAsync(deploymentId, DeploymentStatus.Cancelled, ct);
        }
        catch (Exception ex)
        {
            await log("Error", ex.Message);

            var m = ex.Message.ToLowerInvariant();
            if (m.Contains("access is denied") || m.Contains("permission denied") || m.Contains("unauthorizedaccess"))
                await log("Error",
                    "This host cannot write build files — it looks like the portal is running on shared hosting. " +
                    "Run the CI/CD portal on a machine you control (with git, .NET SDK and Node installed); " +
                    "it will build there and upload the result to your host via FTP/SFTP.");
            else if (m.Contains("is not recognized") || m.Contains("no such file") || m.Contains("cannot find") || m.Contains("enoent"))
                await log("Error",
                    "A build tool was not found on this host (dotnet / npm / node). " +
                    "Run the portal on a machine with the required toolchain installed.");

            await _logs.StatusAsync(deploymentId, DeploymentStatus.Failed, ct);
        }
        finally
        {
            _queue.ClearCancel(deploymentId);
        }
    }

    private async Task RunViaGitHubActions(
        Deployment deployment, Website website, string? pat,
        Func<string, string, Task> log, Func<int, string?, Task> progress, Action checkCancel, CancellationToken ct)
    {
        var repo = website.RepositoryUrl ?? "";
        var branch = deployment.Branch ?? website.DefaultBranch ?? "main";
        var wf = website.WorkflowFile!;

        await log("Info", $"Triggering GitHub Actions workflow '{wf}' on '{branch}'...");
        var dispatchedAt = DateTime.UtcNow;
        await _actions.DispatchAsync(repo, wf, branch, pat, ct);
        await log("Info", "Workflow dispatched. Locating the run on GitHub...");

        long? runId = null;
        for (var i = 0; i < 20 && runId is null; i++)
        {
            checkCancel();
            await Task.Delay(3000, ct);
            runId = await _actions.FindRunAsync(repo, wf, branch, pat, dispatchedAt, ct);
        }
        if (runId is null)
            throw new InvalidOperationException("Workflow was dispatched but its run could not be located on GitHub.");

        var run = await _actions.GetRunAsync(repo, runId.Value, pat, ct);
        if (run is not null)
        {
            await log("Info", $"Run #{run.RunNumber} started — {run.HtmlUrl}");
            if (!string.IsNullOrEmpty(run.HeadSha))
            {
                deployment.CommitId = run.HeadSha;
                await _db.SaveChangesAsync(ct);
            }
        }

        var logged = new HashSet<string>();
        var status = "queued";
        string? conclusion = null;
        var htmlUrl = run?.HtmlUrl ?? "";

        while (true)
        {
            checkCancel();
            var r = await _actions.GetRunAsync(repo, runId.Value, pat, ct);
            if (r is not null) { status = r.Status; conclusion = r.Conclusion; htmlUrl = r.HtmlUrl; }

            var steps = await _actions.GetStepsAsync(repo, runId.Value, pat, ct);
            var completed = 0;
            foreach (var s in steps)
            {
                if (s.Status != "completed") continue;
                completed++;
                if (logged.Add($"{s.Job}/{s.Name}"))
                {
                    var ok = s.Conclusion is "success" or "skipped";
                    await log(ok ? "Info" : "Error", $"{(ok ? "✓" : "✗")} {s.Job} · {s.Name} ({s.Conclusion})");
                }
            }
            await progress((int)(completed * 100.0 / Math.Max(1, steps.Count)), null);

            if (status == "completed") break;
            await Task.Delay(4000, ct);
        }

        if (conclusion == "success")
            await log("Info", $"Workflow succeeded — {htmlUrl}");
        else
            throw new InvalidOperationException($"Workflow concluded '{conclusion}'. Full logs: {htmlUrl}");
    }

    private static string? ResolvePublishFolder(string repoPath, string? configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
            candidates.Add(Path.IsPathRooted(configured) ? configured : Path.Combine(repoPath, configured));
        candidates.AddRange(new[] { "publish", "dist", "build", "wwwroot" }.Select(f => Path.Combine(repoPath, f)));

        return candidates.FirstOrDefault(p => Directory.Exists(p) && Directory.EnumerateFileSystemEntries(p).Any());
    }
}
