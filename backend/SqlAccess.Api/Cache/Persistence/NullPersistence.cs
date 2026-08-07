using SqlAccess.Api.Cache.Interfaces;

namespace SqlAccess.Api.Cache.Persistence;

/// <summary>"Memory only" persistence — discards all durability hooks. Used when Cache:PersistenceMode = None.</summary>
public sealed class NullPersistence : ICachePersistence
{
    /// <inheritdoc />
    public void OnSet(string key, string value, DateTime? expiresAtUtc) { }
    /// <inheritdoc />
    public void OnDelete(string key) { }
    /// <inheritdoc />
    public void OnExpire(string key, DateTime expiresAtUtc) { }
    /// <inheritdoc />
    public void OnFlush() { }
    /// <inheritdoc />
    public Task SaveSnapshotAsync(ICacheStore store, CancellationToken ct) => Task.CompletedTask;
    /// <inheritdoc />
    public void Recover(ICacheStore store) { }
    /// <inheritdoc />
    public void Flush() { }
}
