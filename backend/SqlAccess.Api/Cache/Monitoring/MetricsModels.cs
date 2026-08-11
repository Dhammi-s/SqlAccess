namespace SqlAccess.Api.Cache.Monitoring;

/// <summary>A single command's execution count.</summary>
public sealed record CommandCount(string Command, long Count);

/// <summary>A command that exceeded the slow-command threshold.</summary>
public sealed record SlowCommand(string Command, double Ms, DateTime AtUtc);

/// <summary>A key row for the Key Explorer.</summary>
public sealed record KeyInfo(string Key, int SizeBytes, long TtlSeconds);

/// <summary>A page of keys.</summary>
public sealed record PagedKeys(int Total, int Page, int PageSize, IReadOnlyList<KeyInfo> Items);

/// <summary>A connected TCP client.</summary>
public sealed record ClientInfo(string Id, string RemoteEndpoint, DateTime ConnectedAtUtc, long CommandsProcessed);

/// <summary>An in-memory log/event entry surfaced by /logs.</summary>
public sealed record CacheLogEntry(DateTime TimestampUtc, string Level, string Category, string Message);

/// <summary>Server health.</summary>
public sealed record HealthInfo(string Status, double UptimeSeconds, long Keys, int Clients);

/// <summary>The full live metrics snapshot pushed over SignalR and returned by /stats.</summary>
public sealed record MetricsSnapshot(
    DateTime TimestampUtc,
    double UptimeSeconds,
    long TotalKeys,
    long ExpiredKeys,
    int ConnectedClients,
    long ProcessMemoryBytes,
    long GcHeapBytes,
    double CpuPercent,
    double RequestsPerSecond,
    double AverageLatencyMs,
    long TotalCommands,
    long Hits,
    long Misses,
    double HitRate,
    double MissRate,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    IReadOnlyList<CommandCount> TopCommands,
    IReadOnlyList<SlowCommand> SlowCommands);
