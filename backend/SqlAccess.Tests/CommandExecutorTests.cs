using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Networking;
using SqlAccess.Api.Cache.Persistence;
using SqlAccess.Api.Cache.Services;
using SqlAccess.Tests.Support;

namespace SqlAccess.Tests;

/// <summary>
/// Unit tests for <see cref="CommandExecutor"/> — RESP command dispatch. Asserts on the reply's
/// wire bytes so both the command semantics and the RESP encoding are covered.
/// </summary>
public sealed class CommandExecutorTests
{
    private static (CommandExecutor Exec, ICacheStore Store, StubMonitoring Mon) NewExecutor()
    {
        var store = new InMemoryCacheStore(new NullPersistence());
        var mon = new StubMonitoring();
        return (new CommandExecutor(store, mon), store, mon);
    }

    private static string Wire(RespValue v) => System.Text.Encoding.UTF8.GetString(v.ToBytes());

    [Fact]
    public void Ping_without_args_returns_pong()
    {
        var (exec, _, _) = NewExecutor();
        Assert.Equal("+PONG\r\n", Wire(exec.Execute(["PING"])));
    }

    [Fact]
    public void Ping_with_message_echoes_as_bulk()
    {
        var (exec, _, _) = NewExecutor();
        Assert.Equal("$5\r\nhello\r\n", Wire(exec.Execute(["PING", "hello"])));
    }

    [Fact]
    public void Set_returns_ok_and_stores_value()
    {
        var (exec, store, _) = NewExecutor();

        Assert.Equal("+OK\r\n", Wire(exec.Execute(["SET", "k", "v"])));
        Assert.Equal("v", store.Get("k").Value);
    }

    [Fact]
    public void Set_with_ex_option_applies_ttl()
    {
        var (exec, store, _) = NewExecutor();

        exec.Execute(["SET", "k", "v", "EX", "100"]);

        Assert.InRange(store.Ttl("k"), 90, 100);
    }

    [Fact]
    public void Get_hit_returns_bulk_miss_returns_nil()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "k", "v"]);

        Assert.Equal("$1\r\nv\r\n", Wire(exec.Execute(["GET", "k"])));
        Assert.Equal("$-1\r\n", Wire(exec.Execute(["GET", "absent"])));
    }

    [Fact]
    public void Del_returns_count_of_removed_keys()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "a", "1"]);
        exec.Execute(["SET", "b", "2"]);

        Assert.Equal(":2\r\n", Wire(exec.Execute(["DEL", "a", "b", "missing"])));
    }

    [Fact]
    public void Exists_counts_present_keys()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "a", "1"]);

        Assert.Equal(":1\r\n", Wire(exec.Execute(["EXISTS", "a", "b"])));
    }

    [Fact]
    public void Incr_and_incrby_and_decr()
    {
        var (exec, _, _) = NewExecutor();

        Assert.Equal(":1\r\n", Wire(exec.Execute(["INCR", "n"])));
        Assert.Equal(":6\r\n", Wire(exec.Execute(["INCRBY", "n", "5"])));
        Assert.Equal(":5\r\n", Wire(exec.Execute(["DECR", "n"])));
        Assert.Equal(":2\r\n", Wire(exec.Execute(["DECRBY", "n", "3"])));
    }

    [Fact]
    public void Incr_on_non_integer_returns_error()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "k", "abc"]);

        var reply = Wire(exec.Execute(["INCR", "k"]));

        Assert.StartsWith("-ERR", reply);
    }

    [Fact]
    public void Expire_and_ttl_roundtrip()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "k", "v"]);

        Assert.Equal(":1\r\n", Wire(exec.Execute(["EXPIRE", "k", "50"])));
        var ttlReply = Wire(exec.Execute(["TTL", "k"]));
        Assert.Matches(@"^:(4[0-9]|50)\r\n$", ttlReply);
    }

    [Fact]
    public void Dbsize_reflects_key_count()
    {
        var (exec, _, _) = NewExecutor();
        exec.Execute(["SET", "a", "1"]);
        exec.Execute(["SET", "b", "2"]);

        Assert.Equal(":2\r\n", Wire(exec.Execute(["DBSIZE"])));
    }

    [Fact]
    public void Flush_empties_the_store()
    {
        var (exec, store, _) = NewExecutor();
        exec.Execute(["SET", "a", "1"]);

        Assert.Equal("+OK\r\n", Wire(exec.Execute(["FLUSH"])));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Unknown_command_returns_error()
    {
        var (exec, _, _) = NewExecutor();
        Assert.StartsWith("-ERR unknown command", Wire(exec.Execute(["BOGUS"])));
    }

    [Fact]
    public void Missing_arguments_return_arity_error()
    {
        var (exec, _, _) = NewExecutor();
        Assert.StartsWith("-ERR wrong number of arguments", Wire(exec.Execute(["GET"])));
    }

    [Fact]
    public void Empty_command_returns_error()
    {
        var (exec, _, _) = NewExecutor();
        Assert.Equal("-ERR empty command\r\n", Wire(exec.Execute([])));
    }

    [Fact]
    public void Every_command_is_recorded_with_monitoring()
    {
        var (exec, _, mon) = NewExecutor();

        exec.Execute(["SET", "k", "v"]);
        exec.Execute(["GET", "k"]);

        Assert.Equal(2, mon.RecordedCount);
        Assert.Equal("GET", mon.LastCommand);
    }

    [Fact]
    public void Command_names_are_case_insensitive()
    {
        var (exec, _, _) = NewExecutor();
        Assert.Equal("+OK\r\n", Wire(exec.Execute(["set", "k", "v"])));
        Assert.Equal("$1\r\nv\r\n", Wire(exec.Execute(["get", "k"])));
    }
}
