using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Data;

namespace SqlAccess.Api.Cicd.Services;

public interface ICicdDeploymentService
{
    Task<int> TriggerAsync(int websiteId, string branch, string triggeredBy, CancellationToken ct);
    Task<int?> RetryAsync(int deploymentId, string triggeredBy, CancellationToken ct);
    Task<bool> CancelAsync(int deploymentId, CancellationToken ct);
    Task<List<DeploymentListItem>> ListAsync(int? websiteId, int take, CancellationToken ct);
    Task<DeploymentListItem?> GetAsync(int deploymentId, CancellationToken ct);
    Task<List<LogEntry>> GetLogsAsync(int deploymentId, long afterLogId, CancellationToken ct);
}

public sealed class CicdDeploymentService : ICicdDeploymentService
{
    private readonly AppDbContext _db;
    private readonly IDeploymentQueue _queue;

    public CicdDeploymentService(AppDbContext db, IDeploymentQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task<int> TriggerAsync(int websiteId, string branch, string triggeredBy, CancellationToken ct)
    {
        var site = await _db.Websites.FirstOrDefaultAsync(w => w.WebsiteId == websiteId, ct)
                   ?? throw new InvalidOperationException("Website not found.");

        var deployment = new Deployment
        {
            WebsiteId = websiteId,
            Branch = string.IsNullOrWhiteSpace(branch) ? site.DefaultBranch : branch,
            TriggeredBy = triggeredBy,
            Status = DeploymentStatus.Queued,
            CreatedOn = DateTime.UtcNow,
        };
        _db.Deployments.Add(deployment);
        await _db.SaveChangesAsync(ct);

        await _queue.EnqueueAsync(deployment.DeploymentId, ct);
        return deployment.DeploymentId;
    }

    public async Task<int?> RetryAsync(int deploymentId, string triggeredBy, CancellationToken ct)
    {
        var d = await _db.Deployments.AsNoTracking().FirstOrDefaultAsync(x => x.DeploymentId == deploymentId, ct);
        if (d is null) return null;
        return await TriggerAsync(d.WebsiteId, d.Branch ?? "", triggeredBy, ct);
    }

    public async Task<bool> CancelAsync(int deploymentId, CancellationToken ct)
    {
        var d = await _db.Deployments.FirstOrDefaultAsync(x => x.DeploymentId == deploymentId, ct);
        if (d is null) return false;
        if (d.Status is DeploymentStatus.Queued or DeploymentStatus.Running)
            _queue.RequestCancel(deploymentId);
        return true;
    }

    public async Task<List<DeploymentListItem>> ListAsync(int? websiteId, int take, CancellationToken ct)
    {
        var q = from d in _db.Deployments.AsNoTracking()
                join w in _db.Websites.AsNoTracking() on d.WebsiteId equals w.WebsiteId into wj
                from w in wj.DefaultIfEmpty()
                select new { d, WebsiteName = w != null ? w.WebsiteName : null };

        if (websiteId is not null) q = q.Where(x => x.d.WebsiteId == websiteId);

        var rows = await q.OrderByDescending(x => x.d.DeploymentId).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct);
        return rows.Select(x => Map(x.d, x.WebsiteName)).ToList();
    }

    public async Task<DeploymentListItem?> GetAsync(int deploymentId, CancellationToken ct)
    {
        var d = await _db.Deployments.AsNoTracking().FirstOrDefaultAsync(x => x.DeploymentId == deploymentId, ct);
        if (d is null) return null;
        var name = await _db.Websites.AsNoTracking().Where(w => w.WebsiteId == d.WebsiteId)
            .Select(w => w.WebsiteName).FirstOrDefaultAsync(ct);
        return Map(d, name);
    }

    public async Task<List<LogEntry>> GetLogsAsync(int deploymentId, long afterLogId, CancellationToken ct)
        => await _db.DeploymentLogs.AsNoTracking()
            .Where(l => l.DeploymentId == deploymentId && l.LogId > afterLogId)
            .OrderBy(l => l.LogId)
            .Select(l => new LogEntry(l.LogId, l.Timestamp, l.LogType, l.Message))
            .ToListAsync(ct);

    private static DeploymentListItem Map(Deployment d, string? websiteName)
    {
        double? dur = d.StartedOn is not null && d.FinishedOn is not null
            ? (d.FinishedOn.Value - d.StartedOn.Value).TotalSeconds : null;
        return new DeploymentListItem(
            d.DeploymentId, d.WebsiteId, websiteName, d.Branch, d.CommitId, d.CommitMessage,
            d.TriggeredBy, d.Status, d.StartedOn, d.FinishedOn, dur);
    }
}
