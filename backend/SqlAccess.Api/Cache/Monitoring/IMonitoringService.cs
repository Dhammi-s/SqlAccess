namespace SqlAccess.Api.Cache.Monitoring;

/// <summary>
/// Collects and exposes runtime metrics for the cache: throughput, latency, memory/CPU/GC,
/// hit rate, top/slow commands, connected clients, keys, and a rolling event log.
/// Thread-safe singleton. Delta-based figures (rps, CPU%) are computed per <see cref="GetSnapshot"/> call.
/// </summary>
public interface IMonitoringService
{
    /// <summary>Records one executed command's name and latency (called by the command executor).</summary>
    void RecordCommand(string name, double milliseconds);

    /// <summary>Appends an event to the rolling in-memory log surfaced by <see cref="GetLogs"/>.</summary>
    void Log(string level, string category, string message);

    /// <summary>Computes the current metrics snapshot.</summary>
    MetricsSnapshot GetSnapshot();

    /// <summary>Server health summary.</summary>
    HealthInfo GetHealth();

    /// <summary>A page of keys, optionally filtered by a substring pattern.</summary>
    PagedKeys QueryKeys(string? pattern, int page, int pageSize);

    /// <summary>Snapshot of connected TCP clients.</summary>
    IReadOnlyList<ClientInfo> GetClients();

    /// <summary>The most recent log/event entries (newest first).</summary>
    IReadOnlyList<CacheLogEntry> GetLogs(int take);
}
