using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Workers;

/// <summary>
/// Periodically writes a snapshot of the store (bounding AOF growth) and writes a final snapshot
/// plus flushes the AOF on graceful shutdown. Interval from <see cref="CacheOptions.SnapshotIntervalSeconds"/>.
/// </summary>
public sealed class PersistenceWorker : BackgroundService
{
    private readonly ICachePersistence _persistence;
    private readonly ICacheStore _store;
    private readonly ILogger<PersistenceWorker> _log;
    private readonly TimeSpan _interval;

    public PersistenceWorker(
        ICachePersistence persistence, ICacheStore store,
        IOptions<CacheOptions> options, ILogger<PersistenceWorker> log)
    {
        _persistence = persistence;
        _store = store;
        _log = log;
        var s = options.Value.SnapshotIntervalSeconds;
        _interval = TimeSpan.FromSeconds(s < 5 ? 300 : s);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await _persistence.SaveSnapshotAsync(_store, stoppingToken); }
            catch (Exception ex) { _log.LogError(ex, "Scheduled snapshot failed."); }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _persistence.SaveSnapshotAsync(_store, cancellationToken);
            _persistence.Flush();
        }
        catch (Exception ex) { _log.LogError(ex, "Final snapshot on shutdown failed."); }
        await base.StopAsync(cancellationToken);
    }
}
