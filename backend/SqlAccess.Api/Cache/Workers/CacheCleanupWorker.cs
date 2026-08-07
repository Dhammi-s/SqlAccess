using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Workers;

/// <summary>
/// Background service that periodically evicts expired keys from the store. Complements the lazy
/// expiry done on access, so keys that are never read again are still reclaimed. Interval is
/// configurable via <see cref="CacheOptions.CleanupIntervalSeconds"/>.
/// </summary>
public sealed class CacheCleanupWorker : BackgroundService
{
    private readonly ICacheStore _store;
    private readonly ILogger<CacheCleanupWorker> _log;
    private readonly TimeSpan _interval;

    public CacheCleanupWorker(ICacheStore store, IOptions<CacheOptions> options, ILogger<CacheCleanupWorker> log)
    {
        _store = store;
        _log = log;
        var seconds = options.Value.CleanupIntervalSeconds;
        _interval = TimeSpan.FromSeconds(seconds < 1 ? 15 : seconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var removed = _store.RemoveExpired(DateTime.UtcNow);
                if (removed > 0)
                    _log.LogDebug("Cache cleanup evicted {Count} expired key(s).", removed);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Cache cleanup sweep failed.");
            }
        }
    }
}
