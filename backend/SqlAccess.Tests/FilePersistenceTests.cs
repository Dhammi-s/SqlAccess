using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;
using SqlAccess.Api.Cache.Persistence;
using SqlAccess.Api.Cache.Services;
using SqlAccess.Tests.Support;

namespace SqlAccess.Tests;

/// <summary>
/// Tests <see cref="FilePersistence"/>: the append-only log is written on mutation, snapshots are
/// taken, and recovery folds snapshot + AOF (S/D/E/F) back into a store — the exact sequence that
/// runs when the process restarts. Each test uses an isolated temp directory.
/// </summary>
public sealed class FilePersistenceTests : IDisposable
{
    private readonly string _root;
    private readonly string _dataDir;

    public FilePersistenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sqlaccess-cache-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dataDir = Path.Combine(_root, "cache");
        Directory.CreateDirectory(_dataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private FilePersistence NewPersistence(string mode = "Both")
    {
        var options = Options.Create(new CacheOptions { PersistenceMode = mode, DataDirectory = "cache" });
        var env = new FakeHostEnvironment(_root);
        return new FilePersistence(options, env, NullLogger<FilePersistence>.Instance);
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private string AofPath => Path.Combine(_dataDir, "appendonly.aof");
    private string SnapshotPath => Path.Combine(_dataDir, "snapshot.rdb");

    /// <summary>Reads a file that may still be held open for writing by a live persistence instance.</summary>
    private static string ReadShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }

    [Fact]
    public void OnSet_appends_a_set_line_to_the_aof()
    {
        var persistence = NewPersistence("Aof");
        persistence.Recover(new InMemoryCacheStore(new NullPersistence())); // opens the AOF for append
        persistence.OnSet("k", "v", null);
        persistence.Flush();

        var text = ReadShared(AofPath);

        Assert.Contains($"S {B64("k")} {B64("v")} -", text);
    }

    [Fact]
    public void Recover_replays_set_lines_into_the_store()
    {
        File.WriteAllLines(AofPath,
        [
            $"S {B64("user:1")} {B64("alice")} -",
            $"S {B64("user:2")} {B64("bob")} -",
        ]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.Equal("alice", store.Get("user:1").Value);
        Assert.Equal("bob", store.Get("user:2").Value);
    }

    [Fact]
    public void Recover_applies_delete_after_set()
    {
        File.WriteAllLines(AofPath,
        [
            $"S {B64("k")} {B64("v")} -",
            $"D {B64("k")}",
        ]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.False(store.Exists("k"));
    }

    [Fact]
    public void Recover_applies_flush_clearing_prior_keys()
    {
        File.WriteAllLines(AofPath,
        [
            $"S {B64("a")} {B64("1")} -",
            $"S {B64("b")} {B64("2")} -",
            "F",
            $"S {B64("c")} {B64("3")} -",
        ]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.False(store.Exists("a"));
        Assert.False(store.Exists("b"));
        Assert.Equal("3", store.Get("c").Value);
    }

    [Fact]
    public void Recover_applies_expire_line_to_existing_key()
    {
        var future = DateTime.UtcNow.AddSeconds(120);
        File.WriteAllLines(AofPath,
        [
            $"S {B64("k")} {B64("v")} -",
            $"E {B64("k")} {future.Ticks}",
        ]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.InRange(store.Ttl("k"), 100, 120);
    }

    [Fact]
    public void Recover_skips_entries_that_already_expired()
    {
        var past = DateTime.UtcNow.AddSeconds(-60);
        File.WriteAllLines(AofPath, [$"S {B64("stale")} {B64("v")} {past.Ticks}"]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.False(store.Exists("stale"));
    }

    [Fact]
    public async Task SaveSnapshot_writes_snapshot_and_truncates_aof()
    {
        var persistence = NewPersistence();
        var store = new InMemoryCacheStore(persistence);
        persistence.Recover(store);           // opens AOF for append
        store.Set("k", "v");                  // appends to AOF

        await persistence.SaveSnapshotAsync(store, CancellationToken.None);

        Assert.True(File.Exists(SnapshotPath));
        Assert.Contains($"S {B64("k")} {B64("v")} -", ReadShared(SnapshotPath));
        // AOF is recreated empty after a snapshot captures everything.
        Assert.Equal(string.Empty, ReadShared(AofPath).Trim());
    }

    [Fact]
    public void Recover_folds_snapshot_then_aof()
    {
        // Snapshot has the base state; the AOF holds a post-snapshot overwrite + a new key.
        File.WriteAllLines(SnapshotPath,
        [
            $"S {B64("k")} {B64("old")} -",
        ]);
        File.WriteAllLines(AofPath,
        [
            $"S {B64("k")} {B64("new")} -",
            $"S {B64("fresh")} {B64("1")} -",
        ]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.Equal("new", store.Get("k").Value);   // AOF wins over snapshot
        Assert.Equal("1", store.Get("fresh").Value);
    }

    [Fact]
    public void NullPersistence_writes_nothing()
    {
        ICachePersistence np = new NullPersistence();
        np.OnSet("k", "v", null);
        np.OnDelete("k");
        np.OnFlush();
        np.Flush();

        Assert.Empty(Directory.GetFiles(_dataDir));
    }

    [Fact]
    public void Recover_handles_values_containing_spaces_via_base64()
    {
        File.WriteAllLines(AofPath, [$"S {B64("k")} {B64("hello world with spaces")} -"]);

        var store = new InMemoryCacheStore(new NullPersistence());
        NewPersistence().Recover(store);

        Assert.Equal("hello world with spaces", store.Get("k").Value);
    }
}
