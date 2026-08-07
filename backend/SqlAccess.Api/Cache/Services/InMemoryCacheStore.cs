using System.Collections.Concurrent;
using System.Globalization;
using SqlAccess.Api.Cache.Domain;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Services;

/// <summary>
/// Thread-safe in-memory key/value store backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Scales to millions of keys and concurrent clients. Expiry is handled both lazily (on access) and
/// actively (by <see cref="RemoveExpired"/>, driven by the cleanup worker). Registered as a singleton.
/// </summary>
public sealed class InMemoryCacheStore : ICacheStore
{
    private readonly ConcurrentDictionary<string, CacheEntry> _store = new(StringComparer.Ordinal);
    private readonly ICachePersistence _persistence;

    private long _hits;
    private long _misses;
    private long _expiredRemoved;
    private long _totalCommands;

    /// <summary>Creates the store. The persistence sink receives every mutation (no-op in memory-only mode).</summary>
    public InMemoryCacheStore(ICachePersistence persistence) => _persistence = persistence;

    /// <inheritdoc />
    public long Count => _store.Count;

    /// <inheritdoc />
    public void Set(string key, string value, TimeSpan? ttl = null)
    {
        Interlocked.Increment(ref _totalCommands);
        var expiry = ttl is { } t && t > TimeSpan.Zero ? DateTime.UtcNow.Add(t) : (DateTime?)null;
        _store[key] = new CacheEntry { Value = value, ExpiresAtUtc = expiry };
        _persistence.OnSet(key, value, expiry);
    }

    /// <inheritdoc />
    public (bool Found, string? Value) Get(string key)
    {
        Interlocked.Increment(ref _totalCommands);
        if (_store.TryGetValue(key, out var entry))
        {
            if (entry.IsExpired(DateTime.UtcNow))
            {
                if (_store.TryRemove(key, out _)) Interlocked.Increment(ref _expiredRemoved);
                Interlocked.Increment(ref _misses);
                return (false, null);
            }
            Interlocked.Increment(ref _hits);
            return (true, entry.Value);
        }
        Interlocked.Increment(ref _misses);
        return (false, null);
    }

    /// <inheritdoc />
    public bool Delete(string key)
    {
        Interlocked.Increment(ref _totalCommands);
        if (!_store.TryRemove(key, out var entry)) return false;
        _persistence.OnDelete(key);
        return !entry.IsExpired(DateTime.UtcNow);
    }

    /// <inheritdoc />
    public bool Exists(string key)
    {
        Interlocked.Increment(ref _totalCommands);
        return _store.TryGetValue(key, out var entry) && !entry.IsExpired(DateTime.UtcNow);
    }

    /// <inheritdoc />
    public long Ttl(string key)
    {
        Interlocked.Increment(ref _totalCommands);
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired(DateTime.UtcNow)) return -2; // no such key
        if (entry.ExpiresAtUtc is not { } exp) return -1;                                            // no expiry
        var remaining = (long)Math.Ceiling((exp - DateTime.UtcNow).TotalSeconds);
        return remaining < 0 ? -2 : remaining;
    }

    /// <inheritdoc />
    public bool Expire(string key, TimeSpan ttl)
    {
        Interlocked.Increment(ref _totalCommands);
        var now = DateTime.UtcNow;
        if (!_store.TryGetValue(key, out var entry) || entry.IsExpired(now)) return false;
        entry.ExpiresAtUtc = now.Add(ttl);
        _persistence.OnExpire(key, entry.ExpiresAtUtc.Value);
        return true;
    }

    /// <inheritdoc />
    public long Increment(string key, long by = 1) => AddDelta(key, by);

    /// <inheritdoc />
    public long Decrement(string key, long by = 1) => AddDelta(key, -by);

    /// <summary>Atomic read-modify-write used by INCR/DECR. Retries on contention via AddOrUpdate.</summary>
    private long AddDelta(string key, long delta)
    {
        Interlocked.Increment(ref _totalCommands);
        var now = DateTime.UtcNow;
        long result = 0;
        DateTime? resultExpiry = null;

        _store.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                result = delta;
                resultExpiry = null;
                return new CacheEntry { Value = delta.ToString(CultureInfo.InvariantCulture) };
            },
            updateValueFactory: (_, existing) =>
            {
                long current = 0;
                DateTime? expiry = null;
                if (!existing.IsExpired(now))
                {
                    if (!long.TryParse(existing.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out current))
                        throw new InvalidOperationException("Value is not an integer.");
                    expiry = existing.ExpiresAtUtc; // preserve TTL across increments
                }
                result = current + delta;
                resultExpiry = expiry;
                return new CacheEntry { Value = result.ToString(CultureInfo.InvariantCulture), ExpiresAtUtc = expiry };
            });

        _persistence.OnSet(key, result.ToString(CultureInfo.InvariantCulture), resultExpiry);
        return result;
    }

    /// <inheritdoc />
    public void Flush()
    {
        Interlocked.Increment(ref _totalCommands);
        _store.Clear();
        _persistence.OnFlush();
    }

    /// <inheritdoc />
    public string Ping(string? message = null)
    {
        Interlocked.Increment(ref _totalCommands);
        return string.IsNullOrEmpty(message) ? "PONG" : message;
    }

    /// <inheritdoc />
    public int RemoveExpired(DateTime nowUtc)
    {
        var removed = 0;
        foreach (var kvp in _store)
        {
            if (kvp.Value.IsExpired(nowUtc) && _store.TryRemove(kvp.Key, out _))
                removed++;
        }
        if (removed > 0) Interlocked.Add(ref _expiredRemoved, removed);
        return removed;
    }

    /// <inheritdoc />
    public CacheStatsSnapshot GetStats()
    {
        var hits = Interlocked.Read(ref _hits);
        var misses = Interlocked.Read(ref _misses);
        var total = hits + misses;
        return new CacheStatsSnapshot(
            KeyCount: _store.Count,
            Hits: hits,
            Misses: misses,
            ExpiredRemoved: Interlocked.Read(ref _expiredRemoved),
            TotalCommands: Interlocked.Read(ref _totalCommands),
            HitRate: total == 0 ? 0 : Math.Round((double)hits / total * 100, 2));
    }

    /// <inheritdoc />
    public IEnumerable<(string Key, string Value, DateTime? ExpiresAtUtc)> Export()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _store)
            if (!kvp.Value.IsExpired(now))
                yield return (kvp.Key, kvp.Value.Value, kvp.Value.ExpiresAtUtc);
    }

    /// <inheritdoc />
    public void LoadForRecovery(string key, string value, DateTime? expiresAtUtc)
    {
        if (expiresAtUtc is { } exp && exp <= DateTime.UtcNow) return; // already expired — skip
        _store[key] = new CacheEntry { Value = value, ExpiresAtUtc = expiresAtUtc };
    }
}
