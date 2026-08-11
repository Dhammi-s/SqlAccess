using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Persistence;
using SqlAccess.Api.Cache.Services;

namespace SqlAccess.Tests;

/// <summary>
/// Unit tests for <see cref="InMemoryCacheStore"/> — the core engine. Uses <see cref="NullPersistence"/>
/// so nothing touches disk; each test gets a fresh store.
/// </summary>
public sealed class CacheStoreTests
{
    private static ICacheStore NewStore() => new InMemoryCacheStore(new NullPersistence());

    [Fact]
    public void Set_then_Get_returns_value()
    {
        var store = NewStore();
        store.Set("k", "v");

        var (found, value) = store.Get("k");

        Assert.True(found);
        Assert.Equal("v", value);
    }

    [Fact]
    public void Get_missing_key_returns_not_found()
    {
        var (found, value) = NewStore().Get("nope");

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Set_overwrites_existing_value()
    {
        var store = NewStore();
        store.Set("k", "first");
        store.Set("k", "second");

        Assert.Equal("second", store.Get("k").Value);
    }

    [Fact]
    public void Delete_removes_live_key_and_reports_true()
    {
        var store = NewStore();
        store.Set("k", "v");

        Assert.True(store.Delete("k"));
        Assert.False(store.Exists("k"));
    }

    [Fact]
    public void Delete_missing_key_reports_false()
    {
        Assert.False(NewStore().Delete("ghost"));
    }

    [Fact]
    public void Exists_is_false_for_missing_and_true_for_present()
    {
        var store = NewStore();
        Assert.False(store.Exists("k"));
        store.Set("k", "v");
        Assert.True(store.Exists("k"));
    }

    [Fact]
    public void Ttl_returns_minus2_for_missing_key()
    {
        Assert.Equal(-2, NewStore().Ttl("missing"));
    }

    [Fact]
    public void Ttl_returns_minus1_for_key_without_expiry()
    {
        var store = NewStore();
        store.Set("k", "v");
        Assert.Equal(-1, store.Ttl("k"));
    }

    [Fact]
    public void Ttl_returns_positive_seconds_for_key_with_expiry()
    {
        var store = NewStore();
        store.Set("k", "v", TimeSpan.FromSeconds(100));

        var ttl = store.Ttl("k");

        Assert.InRange(ttl, 90, 100);
    }

    [Fact]
    public void Expired_key_is_not_returned_by_Get()
    {
        var store = NewStore();
        store.Set("k", "v", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(20);

        Assert.False(store.Get("k").Found);
    }

    [Fact]
    public void Expire_sets_ttl_on_existing_key()
    {
        var store = NewStore();
        store.Set("k", "v");

        Assert.True(store.Expire("k", TimeSpan.FromSeconds(50)));
        Assert.InRange(store.Ttl("k"), 40, 50);
    }

    [Fact]
    public void Expire_on_missing_key_returns_false()
    {
        Assert.False(NewStore().Expire("missing", TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void Increment_missing_key_starts_at_delta()
    {
        var store = NewStore();
        Assert.Equal(1, store.Increment("counter"));
        Assert.Equal(2, store.Increment("counter"));
    }

    [Fact]
    public void Increment_by_amount_and_decrement()
    {
        var store = NewStore();
        store.Set("n", "10");

        Assert.Equal(15, store.Increment("n", 5));
        Assert.Equal(12, store.Decrement("n", 3));
    }

    [Fact]
    public void Increment_preserves_ttl()
    {
        var store = NewStore();
        store.Set("n", "1", TimeSpan.FromSeconds(100));

        store.Increment("n");

        Assert.InRange(store.Ttl("n"), 90, 100);
    }

    [Fact]
    public void Increment_on_non_integer_throws_InvalidOperationException()
    {
        var store = NewStore();
        store.Set("k", "not-a-number");

        Assert.Throws<InvalidOperationException>(() => store.Increment("k"));
    }

    [Fact]
    public void Flush_removes_all_keys()
    {
        var store = NewStore();
        store.Set("a", "1");
        store.Set("b", "2");

        store.Flush();

        Assert.Equal(0, store.Count);
        Assert.False(store.Exists("a"));
    }

    [Fact]
    public void Ping_echoes_message_or_returns_pong()
    {
        var store = NewStore();
        Assert.Equal("PONG", store.Ping());
        Assert.Equal("hi", store.Ping("hi"));
    }

    [Fact]
    public void RemoveExpired_sweeps_only_expired_entries()
    {
        var store = NewStore();
        store.Set("live", "v");
        store.Set("dead", "v", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(20);

        var removed = store.RemoveExpired(DateTime.UtcNow);

        Assert.Equal(1, removed);
        Assert.True(store.Exists("live"));
    }

    [Fact]
    public void Export_yields_live_entries_and_skips_expired()
    {
        var store = NewStore();
        store.Set("live", "v");
        store.Set("dead", "v", TimeSpan.FromMilliseconds(1));
        Thread.Sleep(20);

        var keys = store.Export().Select(e => e.Key).ToList();

        Assert.Contains("live", keys);
        Assert.DoesNotContain("dead", keys);
    }

    [Fact]
    public void GetStats_tracks_hits_and_misses()
    {
        var store = NewStore();
        store.Set("k", "v");
        store.Get("k");      // hit
        store.Get("absent"); // miss

        var stats = store.GetStats();

        Assert.Equal(1, stats.Hits);
        Assert.Equal(1, stats.Misses);
        Assert.Equal(50, stats.HitRate);
    }

    [Fact]
    public void Concurrent_increments_are_atomic()
    {
        var store = NewStore();
        const int threads = 8, perThread = 1000;

        Parallel.For(0, threads, _ =>
        {
            for (var i = 0; i < perThread; i++) store.Increment("counter");
        });

        Assert.Equal(threads * perThread, store.Increment("counter") - 1);
    }
}
