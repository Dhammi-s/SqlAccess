using System.Globalization;
using SqlAccess.Api.Cache.Interfaces;

namespace SqlAccess.Api.Cache.Networking;

/// <summary>
/// Parses a command's argument array and executes it against the <see cref="ICacheStore"/>,
/// returning the RESP reply. Stateless and thread-safe; registered as a singleton.
/// </summary>
public sealed class CommandExecutor
{
    private readonly ICacheStore _store;

    public CommandExecutor(ICacheStore store) => _store = store;

    /// <summary>Executes one command. Never throws — protocol/usage errors become RESP error replies.</summary>
    public RespValue Execute(string[] args)
    {
        if (args.Length == 0) return RespValue.Error("ERR empty command");
        var cmd = args[0].ToUpperInvariant();

        try
        {
            return cmd switch
            {
                "PING" => args.Length > 1 ? RespValue.Bulk(args[1]) : RespValue.Simple("PONG"),
                "SET" => Set(args),
                "GET" => Get(args),
                "DEL" => Del(args),
                "EXISTS" => Exists(args),
                "TTL" => Require(args, 2) ?? RespValue.Integer(_store.Ttl(args[1])),
                "EXPIRE" => Expire(args),
                "INCR" => Require(args, 2) ?? RespValue.Integer(_store.Increment(args[1])),
                "DECR" => Require(args, 2) ?? RespValue.Integer(_store.Decrement(args[1])),
                "INCRBY" => IncrBy(args, 1),
                "DECRBY" => IncrBy(args, -1),
                "DBSIZE" => RespValue.Integer(_store.Count),
                "FLUSH" or "FLUSHALL" or "FLUSHDB" => Flush(),
                "QUIT" => RespValue.Simple("OK"),
                "COMMAND" => RespValue.Array([]), // redis-cli sends COMMAND DOCS on connect
                _ => RespValue.Error($"ERR unknown command '{cmd}'"),
            };
        }
        catch (InvalidOperationException ex)
        {
            return RespValue.Error("ERR " + ex.Message);
        }
        catch (FormatException)
        {
            return RespValue.Error("ERR value is not an integer or out of range");
        }
    }

    private static RespValue? Require(string[] args, int min)
        => args.Length < min ? RespValue.Error($"ERR wrong number of arguments for '{args[0].ToLowerInvariant()}'") : null;

    private RespValue Set(string[] args)
    {
        if (Require(args, 3) is { } err) return err;
        TimeSpan? ttl = null;
        for (var i = 3; i < args.Length;)
        {
            var opt = args[i].ToUpperInvariant();
            if (opt == "EX" && i + 1 < args.Length) { ttl = TimeSpan.FromSeconds(long.Parse(args[i + 1], CultureInfo.InvariantCulture)); i += 2; }
            else if (opt == "PX" && i + 1 < args.Length) { ttl = TimeSpan.FromMilliseconds(long.Parse(args[i + 1], CultureInfo.InvariantCulture)); i += 2; }
            else return RespValue.Error("ERR syntax error");
        }
        _store.Set(args[1], args[2], ttl);
        return RespValue.Simple("OK");
    }

    private RespValue Get(string[] args)
    {
        if (Require(args, 2) is { } err) return err;
        var (found, value) = _store.Get(args[1]);
        return found ? RespValue.Bulk(value) : RespValue.Nil;
    }

    private RespValue Del(string[] args)
    {
        if (Require(args, 2) is { } err) return err;
        long removed = 0;
        for (var i = 1; i < args.Length; i++) if (_store.Delete(args[i])) removed++;
        return RespValue.Integer(removed);
    }

    private RespValue Exists(string[] args)
    {
        if (Require(args, 2) is { } err) return err;
        long count = 0;
        for (var i = 1; i < args.Length; i++) if (_store.Exists(args[i])) count++;
        return RespValue.Integer(count);
    }

    private RespValue Expire(string[] args)
    {
        if (Require(args, 3) is { } err) return err;
        var seconds = long.Parse(args[2], CultureInfo.InvariantCulture);
        return RespValue.Integer(_store.Expire(args[1], TimeSpan.FromSeconds(seconds)) ? 1 : 0);
    }

    private RespValue IncrBy(string[] args, int sign)
    {
        if (Require(args, 3) is { } err) return err;
        var by = long.Parse(args[2], CultureInfo.InvariantCulture) * sign;
        return RespValue.Integer(_store.Increment(args[1], by));
    }

    private RespValue Flush()
    {
        _store.Flush();
        return RespValue.Simple("OK");
    }
}
