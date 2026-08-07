using System.ComponentModel.DataAnnotations;

namespace SqlAccess.Api.Cache.Models;

/// <summary>Options for the in-memory cache, bound from the "Cache" configuration section.</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>How often the background worker actively sweeps expired keys. Default 15s.</summary>
    public int CleanupIntervalSeconds { get; set; } = 15;

    /// <summary>Enable the TCP/RESP server. Default true.</summary>
    public bool TcpEnabled { get; set; } = true;

    /// <summary>TCP port for the RESP server. Default 6380 (avoids clashing with a real Redis on 6379).</summary>
    public int TcpPort { get; set; } = 6380;

    /// <summary>Bind address. Default 127.0.0.1 (loopback) since the TCP protocol itself is unauthenticated.</summary>
    public string TcpBindAddress { get; set; } = "127.0.0.1";

    /// <summary>Persistence mode: "None" (memory only), "Aof", "Snapshot", or "Both". Default "Both".</summary>
    public string PersistenceMode { get; set; } = "Both";

    /// <summary>Directory for the AOF/snapshot files. Relative paths resolve under the app content root.</summary>
    public string DataDirectory { get; set; } = "App_Data/cache";

    /// <summary>How often to write a full snapshot (and truncate the AOF). Default 300s.</summary>
    public int SnapshotIntervalSeconds { get; set; } = 300;
}

/// <summary>Point-in-time counters for the store. Extended by the monitoring phase.</summary>
public sealed record CacheStatsSnapshot(
    long KeyCount,
    long Hits,
    long Misses,
    long ExpiredRemoved,
    long TotalCommands,
    double HitRate);

// ---------- REST DTOs (Phase 1 facade to exercise the commands) ----------

/// <summary>Request body for SET.</summary>
public sealed class SetRequest
{
    [Required] public string Key { get; set; } = string.Empty;
    [Required] public string Value { get; set; } = string.Empty;
    /// <summary>Optional time-to-live in seconds. Omit or 0 for no expiry.</summary>
    public int? TtlSeconds { get; set; }
}

/// <summary>Request body for EXPIRE.</summary>
public sealed record ExpireRequest([Required] string Key, [Required] int TtlSeconds);

/// <summary>Generic command result.</summary>
public sealed record CommandResult(bool Ok, string? Value = null, long? Number = null, string? Message = null);
