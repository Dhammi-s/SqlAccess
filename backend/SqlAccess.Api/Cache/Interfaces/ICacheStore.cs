using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Interfaces;

/// <summary>
/// The in-memory key/value store engine. Implementations must be thread-safe and support
/// concurrent readers/writers. Operations are synchronous by design — they are pure in-memory
/// and adding async would only add allocation/latency with no benefit.
/// </summary>
public interface ICacheStore
{
    /// <summary>SET key = value, with an optional TTL. Overwrites any existing value.</summary>
    void Set(string key, string value, TimeSpan? ttl = null);

    /// <summary>GET key. Returns found=false for a missing or expired key.</summary>
    (bool Found, string? Value) Get(string key);

    /// <summary>DEL key. Returns true if a live key was removed.</summary>
    bool Delete(string key);

    /// <summary>EXISTS key. Returns true if the key exists and is not expired.</summary>
    bool Exists(string key);

    /// <summary>TTL key, in seconds: -2 = no such key, -1 = exists with no expiry, otherwise seconds remaining.</summary>
    long Ttl(string key);

    /// <summary>EXPIRE — set/replace the TTL on an existing key. Returns false if the key does not exist.</summary>
    bool Expire(string key, TimeSpan ttl);

    /// <summary>INCR key by <paramref name="by"/>. Missing key starts at 0. Throws if the value is not an integer.</summary>
    long Increment(string key, long by = 1);

    /// <summary>DECR key by <paramref name="by"/>. Missing key starts at 0. Throws if the value is not an integer.</summary>
    long Decrement(string key, long by = 1);

    /// <summary>FLUSH — remove all keys.</summary>
    void Flush();

    /// <summary>PING — liveness check. Echoes the message, or returns "PONG".</summary>
    string Ping(string? message = null);

    /// <summary>Current number of keys (may include not-yet-swept expired keys).</summary>
    long Count { get; }

    /// <summary>Actively removes expired entries; returns the number removed. Called by the cleanup worker.</summary>
    int RemoveExpired(DateTime nowUtc);

    /// <summary>A snapshot of the store's counters.</summary>
    CacheStatsSnapshot GetStats();

    /// <summary>Exports all live (non-expired) entries — used to write a snapshot.</summary>
    IEnumerable<(string Key, string Value, DateTime? ExpiresAtUtc)> Export();

    /// <summary>Loads an entry directly during recovery: no persistence re-append, no stats, skips already-expired entries.</summary>
    void LoadForRecovery(string key, string value, DateTime? expiresAtUtc);
}
