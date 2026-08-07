namespace SqlAccess.Api.Cache.Interfaces;

/// <summary>
/// Durability sink for the store. The store calls the On* hooks on every mutation (append-only log);
/// the snapshot + recovery methods take/restore a full point-in-time image. Implementations must be
/// thread-safe. A no-op implementation provides "memory only" mode.
/// </summary>
public interface ICachePersistence
{
    /// <summary>Record a SET (or the result of INCR/DECR) with its optional absolute expiry.</summary>
    void OnSet(string key, string value, DateTime? expiresAtUtc);

    /// <summary>Record a key deletion.</summary>
    void OnDelete(string key);

    /// <summary>Record a new/updated expiry on an existing key.</summary>
    void OnExpire(string key, DateTime expiresAtUtc);

    /// <summary>Record a flush (all keys removed).</summary>
    void OnFlush();

    /// <summary>Write a full snapshot of the store and truncate the append-only log.</summary>
    Task SaveSnapshotAsync(ICacheStore store, CancellationToken ct);

    /// <summary>Restore state at startup: load the snapshot, then replay the append-only log. Runs before serving.</summary>
    void Recover(ICacheStore store);

    /// <summary>Flush any buffered writes to disk (called on graceful shutdown).</summary>
    void Flush();
}
