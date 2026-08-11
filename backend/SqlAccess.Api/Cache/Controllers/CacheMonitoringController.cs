using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Models;
using SqlAccess.Api.Cache.Monitoring;

namespace SqlAccess.Api.Cache.Controllers;

/// <summary>Read-only monitoring endpoints for the cache dashboard.</summary>
[ApiController]
[Authorize]
[Route("api/cache")]
public sealed class CacheMonitoringController : ControllerBase
{
    private readonly IMonitoringService _monitoring;
    private readonly CacheOptions _options;

    public CacheMonitoringController(IMonitoringService monitoring, IOptions<CacheOptions> options)
    {
        _monitoring = monitoring;
        _options = options.Value;
    }

    /// <summary>Full live metrics snapshot.</summary>
    [HttpGet("stats")]
    public ActionResult<MetricsSnapshot> Stats() => Ok(_monitoring.GetSnapshot());

    /// <summary>Paged, optionally filtered key list.</summary>
    [HttpGet("keys")]
    public ActionResult<PagedKeys> Keys([FromQuery] string? pattern, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(_monitoring.QueryKeys(pattern, page, pageSize));

    /// <summary>Connected TCP clients.</summary>
    [HttpGet("clients")]
    public ActionResult<IReadOnlyList<ClientInfo>> Clients() => Ok(_monitoring.GetClients());

    /// <summary>Effective cache configuration.</summary>
    [HttpGet("config")]
    public ActionResult<object> Config() => Ok(new
    {
        _options.CleanupIntervalSeconds,
        _options.TcpEnabled,
        _options.TcpPort,
        _options.TcpBindAddress,
        _options.PersistenceMode,
        _options.DataDirectory,
        _options.SnapshotIntervalSeconds,
    });

    /// <summary>Server health.</summary>
    [HttpGet("health")]
    public ActionResult<HealthInfo> Health() => Ok(_monitoring.GetHealth());

    /// <summary>Recent log/event entries (newest first).</summary>
    [HttpGet("logs")]
    public ActionResult<IReadOnlyList<CacheLogEntry>> Logs([FromQuery] int take = 100)
        => Ok(_monitoring.GetLogs(take));
}
