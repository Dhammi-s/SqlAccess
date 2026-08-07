namespace SqlAccess.Api.Cache.Domain;

/// <summary>
/// A single value held in the in-memory store, with an optional absolute expiry.
/// Values are stored as strings (the wire/command protocol is text-based, like Redis).
/// </summary>
public sealed class CacheEntry
{
    /// <summary>The stored value.</summary>
    public required string Value { get; set; }

    /// <summary>Absolute UTC time the entry expires, or <c>null</c> for no expiry (persist until deleted).</summary>
    public DateTime? ExpiresAtUtc { get; set; }

    /// <summary>UTC time the entry was created or last written.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>True when the entry has an expiry that is at or before <paramref name="nowUtc"/>.</summary>
    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc is { } exp && exp <= nowUtc;
}
