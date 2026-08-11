using System.Collections.Concurrent;
using System.Diagnostics;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Networking;

namespace SqlAccess.Api.Cache.Monitoring;

/// <inheritdoc />
public sealed class MonitoringService : IMonitoringService
{
    private const double SlowThresholdMs = 5.0;
    private const int MaxSlow = 20;
    private const int MaxLog = 500;

    private readonly ICacheStore _store;
    private readonly IConnectionManager _connections;

    private readonly DateTime _startUtc = DateTime.UtcNow;
    private readonly ConcurrentDictionary<string, long> _commandCounts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SlowCommand> _slow = new();
    private readonly ConcurrentQueue<CacheLogEntry> _logs = new();

    private long _totalLatencyMicros;
    private long _latencyCount;

    // Delta baselines (guarded by _deltaLock).
    private readonly object _deltaLock = new();
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private long _lastTotalCommands;
    private TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;

    public MonitoringService(ICacheStore store, IConnectionManager connections)
    {
        _store = store;
        _connections = connections;
    }

    /// <inheritdoc />
    public void RecordCommand(string name, double milliseconds)
    {
        _commandCounts.AddOrUpdate(name, 1, (_, c) => c + 1);
        Interlocked.Add(ref _totalLatencyMicros, (long)(milliseconds * 1000));
        Interlocked.Increment(ref _latencyCount);

        if (milliseconds >= SlowThresholdMs)
        {
            _slow.Enqueue(new SlowCommand(name, Math.Round(milliseconds, 3), DateTime.UtcNow));
            while (_slow.Count > MaxSlow) _slow.TryDequeue(out _);
            Log("Warning", "Performance", $"Slow command {name} took {milliseconds:F2} ms");
        }
    }

    /// <inheritdoc />
    public void Log(string level, string category, string message)
    {
        _logs.Enqueue(new CacheLogEntry(DateTime.UtcNow, level, category, message));
        while (_logs.Count > MaxLog) _logs.TryDequeue(out _);
    }

    /// <inheritdoc />
    public MetricsSnapshot GetSnapshot()
    {
        var now = DateTime.UtcNow;
        var stats = _store.GetStats();
        var proc = Process.GetCurrentProcess();

        double rps, cpuPercent;
        lock (_deltaLock)
        {
            var elapsed = Math.Max(0.001, (now - _lastSampleUtc).TotalSeconds);
            rps = Math.Round((stats.TotalCommands - _lastTotalCommands) / elapsed, 1);

            var cpuNow = proc.TotalProcessorTime;
            var cpuDelta = (cpuNow - _lastCpuTime).TotalSeconds;
            cpuPercent = Math.Round(cpuDelta / elapsed / Environment.ProcessorCount * 100, 1);

            _lastSampleUtc = now;
            _lastTotalCommands = stats.TotalCommands;
            _lastCpuTime = cpuNow;
        }

        var latCount = Interlocked.Read(ref _latencyCount);
        var avgLatency = latCount == 0 ? 0 : Math.Round(Interlocked.Read(ref _totalLatencyMicros) / 1000.0 / latCount, 3);

        var top = _commandCounts
            .Select(kvp => new CommandCount(kvp.Key, kvp.Value))
            .OrderByDescending(c => c.Count)
            .Take(5)
            .ToList();

        var missRate = Math.Round(100 - stats.HitRate, 2);

        return new MetricsSnapshot(
            TimestampUtc: now,
            UptimeSeconds: Math.Round((now - _startUtc).TotalSeconds, 0),
            TotalKeys: stats.KeyCount,
            ExpiredKeys: stats.ExpiredRemoved,
            ConnectedClients: _connections.Count,
            ProcessMemoryBytes: proc.WorkingSet64,
            GcHeapBytes: GC.GetTotalMemory(false),
            CpuPercent: Math.Clamp(cpuPercent, 0, 100),
            RequestsPerSecond: rps < 0 ? 0 : rps,
            AverageLatencyMs: avgLatency,
            TotalCommands: stats.TotalCommands,
            Hits: stats.Hits,
            Misses: stats.Misses,
            HitRate: stats.HitRate,
            MissRate: stats.Hits + stats.Misses == 0 ? 0 : missRate,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            TopCommands: top,
            SlowCommands: _slow.Reverse().ToList());
    }

    /// <inheritdoc />
    public HealthInfo GetHealth()
        => new("Healthy", Math.Round((DateTime.UtcNow - _startUtc).TotalSeconds, 0), _store.Count, _connections.Count);

    /// <inheritdoc />
    public PagedKeys QueryKeys(string? pattern, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 500);
        var now = DateTime.UtcNow;

        var query = _store.Export();
        if (!string.IsNullOrWhiteSpace(pattern))
        {
            var p = pattern.Trim();
            query = query.Where(e => e.Key.Contains(p, StringComparison.OrdinalIgnoreCase));
        }

        var all = query.OrderBy(e => e.Key).ToList();
        var items = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new KeyInfo(
                e.Key,
                System.Text.Encoding.UTF8.GetByteCount(e.Value),
                e.ExpiresAtUtc is { } exp ? Math.Max(-1, (long)Math.Ceiling((exp - now).TotalSeconds)) : -1))
            .ToList();

        return new PagedKeys(all.Count, page, pageSize, items);
    }

    /// <inheritdoc />
    public IReadOnlyList<ClientInfo> GetClients()
        => _connections.Snapshot()
            .OrderBy(c => c.ConnectedAtUtc)
            .Select(c => new ClientInfo(c.Id, c.RemoteEndpoint, c.ConnectedAtUtc, c.CommandsProcessed))
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<CacheLogEntry> GetLogs(int take)
        => _logs.Reverse().Take(Math.Clamp(take, 1, MaxLog)).ToList();
}
