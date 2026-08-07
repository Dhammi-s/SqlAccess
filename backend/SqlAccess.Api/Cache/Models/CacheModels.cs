using System.ComponentModel.DataAnnotations;

namespace SqlAccess.Api.Cache.Models;

/// <summary>Options for the in-memory cache, bound from the "Cache" configuration section.</summary>
public sealed class CacheOptions
{
    public const string SectionName = "Cache";

    /// <summary>How often the background worker actively sweeps expired keys. Default 15s.</summary>
    public int CleanupIntervalSeconds { get; set; } = 15;
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
