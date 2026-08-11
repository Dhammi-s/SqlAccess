using Microsoft.AspNetCore.SignalR;
using SqlAccess.Api.Cache.Hubs;
using SqlAccess.Api.Cache.Monitoring;

namespace SqlAccess.Api.Cache.Workers;

/// <summary>Pushes a fresh <see cref="MetricsSnapshot"/> to all SignalR clients once per second.</summary>
public sealed class MetricsBroadcastWorker : BackgroundService
{
    private readonly IHubContext<CacheMetricsHub> _hub;
    private readonly IMonitoringService _monitoring;
    private readonly ILogger<MetricsBroadcastWorker> _log;

    public MetricsBroadcastWorker(
        IHubContext<CacheMetricsHub> hub, IMonitoringService monitoring, ILogger<MetricsBroadcastWorker> log)
    {
        _hub = hub;
        _monitoring = monitoring;
        _log = log;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _hub.Clients.All.SendAsync("metrics", _monitoring.GetSnapshot(), stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to broadcast cache metrics.");
            }
        }
    }
}
