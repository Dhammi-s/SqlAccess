using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Controllers;

/// <summary>
/// REST facade over the in-memory store commands (Phase 1). Lets clients exercise the store over HTTP;
/// the TCP/RESP server and monitoring endpoints arrive in later phases. Requires an authenticated user.
/// </summary>
[ApiController]
[Authorize]
[Route("api/cache")]
public sealed class CacheController : ControllerBase
{
    private readonly ICacheStore _store;
    private readonly ICachePersistence _persistence;

    public CacheController(ICacheStore store, ICachePersistence persistence)
    {
        _store = store;
        _persistence = persistence;
    }

    /// <summary>SAVE — write a snapshot now and truncate the append-only log.</summary>
    [HttpPost("save")]
    public async Task<ActionResult<CommandResult>> Save(CancellationToken ct)
    {
        await _persistence.SaveSnapshotAsync(_store, ct);
        return Ok(new CommandResult(true, Message: "Snapshot saved"));
    }

    /// <summary>PING — liveness check.</summary>
    [HttpGet("ping")]
    public ActionResult<CommandResult> Ping([FromQuery] string? message = null)
        => Ok(new CommandResult(true, Value: _store.Ping(message)));

    /// <summary>SET key = value with optional TTL.</summary>
    [HttpPost("set")]
    public ActionResult<CommandResult> Set([FromBody] SetRequest req)
    {
        var ttl = req.TtlSeconds is > 0 ? TimeSpan.FromSeconds(req.TtlSeconds.Value) : (TimeSpan?)null;
        _store.Set(req.Key, req.Value, ttl);
        return Ok(new CommandResult(true, Message: "OK"));
    }

    /// <summary>GET key.</summary>
    [HttpGet("get/{key}")]
    public ActionResult<CommandResult> Get(string key)
    {
        var (found, value) = _store.Get(key);
        return found ? Ok(new CommandResult(true, Value: value)) : NotFound(new CommandResult(false, Message: "(nil)"));
    }

    /// <summary>DEL key.</summary>
    [HttpDelete("del/{key}")]
    public ActionResult<CommandResult> Delete(string key)
        => Ok(new CommandResult(true, Number: _store.Delete(key) ? 1 : 0));

    /// <summary>EXISTS key.</summary>
    [HttpGet("exists/{key}")]
    public ActionResult<CommandResult> Exists(string key)
        => Ok(new CommandResult(true, Number: _store.Exists(key) ? 1 : 0));

    /// <summary>TTL key (seconds; -1 no expiry, -2 no key).</summary>
    [HttpGet("ttl/{key}")]
    public ActionResult<CommandResult> Ttl(string key)
        => Ok(new CommandResult(true, Number: _store.Ttl(key)));

    /// <summary>EXPIRE key ttlSeconds.</summary>
    [HttpPost("expire")]
    public ActionResult<CommandResult> Expire([FromBody] ExpireRequest req)
        => Ok(new CommandResult(true, Number: _store.Expire(req.Key, TimeSpan.FromSeconds(req.TtlSeconds)) ? 1 : 0));

    /// <summary>INCR key.</summary>
    [HttpPost("incr/{key}")]
    public ActionResult<CommandResult> Incr(string key)
    {
        try { return Ok(new CommandResult(true, Number: _store.Increment(key))); }
        catch (InvalidOperationException ex) { return BadRequest(new CommandResult(false, Message: ex.Message)); }
    }

    /// <summary>DECR key.</summary>
    [HttpPost("decr/{key}")]
    public ActionResult<CommandResult> Decr(string key)
    {
        try { return Ok(new CommandResult(true, Number: _store.Decrement(key))); }
        catch (InvalidOperationException ex) { return BadRequest(new CommandResult(false, Message: ex.Message)); }
    }

    /// <summary>FLUSH — clear all keys.</summary>
    [HttpPost("flush")]
    public ActionResult<CommandResult> Flush()
    {
        _store.Flush();
        return Ok(new CommandResult(true, Message: "OK"));
    }
}
