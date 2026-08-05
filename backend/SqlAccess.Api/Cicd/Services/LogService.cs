using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Hubs;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Data;

namespace SqlAccess.Api.Cicd.Services;

public interface ILogService
{
    Task LogAsync(int deploymentId, string type, string message, CancellationToken ct = default);
    Task ProgressAsync(int deploymentId, int percent, string? label = null, CancellationToken ct = default);
    Task StatusAsync(int deploymentId, string status, CancellationToken ct = default);
}

/// <summary>Persists each log line to SQL and broadcasts it to the deployment's SignalR group.</summary>
public sealed class LogService : ILogService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<DeploymentHub> _hub;

    public LogService(IServiceScopeFactory scopeFactory, IHubContext<DeploymentHub> hub)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
    }

    public async Task LogAsync(int deploymentId, string type, string message, CancellationToken ct = default)
    {
        var ts = DateTime.UtcNow;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DeploymentLogs.Add(new DeploymentLog
            {
                DeploymentId = deploymentId,
                Timestamp = ts,
                LogType = type,
                Message = message,
            });
            await db.SaveChangesAsync(ct);
        }

        await _hub.Clients.Group(DeploymentHub.Group(deploymentId))
            .SendAsync("log", new { timestamp = ts, logType = type, message }, ct);
    }

    public Task ProgressAsync(int deploymentId, int percent, string? label = null, CancellationToken ct = default)
        => _hub.Clients.Group(DeploymentHub.Group(deploymentId))
            .SendAsync("progress", new { percent, label }, ct);

    public async Task StatusAsync(int deploymentId, string status, CancellationToken ct = default)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var d = await db.Deployments.FirstOrDefaultAsync(x => x.DeploymentId == deploymentId, ct);
            if (d is not null)
            {
                d.Status = status;
                if (status == DeploymentStatus.Running && d.StartedOn is null) d.StartedOn = DateTime.UtcNow;
                if (status is DeploymentStatus.Success or DeploymentStatus.Failed or DeploymentStatus.Cancelled)
                    d.FinishedOn = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        await _hub.Clients.Group(DeploymentHub.Group(deploymentId))
            .SendAsync("status", new { deploymentId, status }, ct);
    }
}
