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

    public DeploymentOrchestrator(
        AppDbContext db, ILogService logs, IGitService git, IBuildService build,
        IEncryptionService enc, IDeploymentQueue queue, IEnumerable<IDeploymentProvider> providers)
    {
        _db = db;
        _logs = logs;
        _git = git;
        _build = build;
        _enc = enc;
        _queue = queue;
        _providers = providers;
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
            await _logs.StatusAsync(deploymentId, DeploymentStatus.Failed, ct);
        }
        finally
        {
            _queue.ClearCancel(deploymentId);
        }
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
