using Microsoft.Extensions.Hosting;
using SqlAccess.Api.Cache.Monitoring;

namespace SqlAccess.Tests.Support;

/// <summary>
/// A no-op <see cref="IMonitoringService"/> for unit tests that need to construct a
/// <c>CommandExecutor</c> without pulling in the real metrics pipeline. Records the last
/// command so tests can assert the executor invokes the monitoring hook.
/// </summary>
public sealed class StubMonitoring : IMonitoringService
{
    public string? LastCommand { get; private set; }
    public int RecordedCount { get; private set; }

    public void RecordCommand(string name, double milliseconds)
    {
        LastCommand = name;
        RecordedCount++;
    }

    public void Log(string level, string category, string message) { }
    public MetricsSnapshot GetSnapshot() => throw new NotSupportedException();
    public HealthInfo GetHealth() => throw new NotSupportedException();
    public PagedKeys QueryKeys(string? pattern, int page, int pageSize) => throw new NotSupportedException();
    public IReadOnlyList<ClientInfo> GetClients() => Array.Empty<ClientInfo>();
    public IReadOnlyList<CacheLogEntry> GetLogs(int take) => Array.Empty<CacheLogEntry>();
}

/// <summary>Minimal <see cref="IHostEnvironment"/> pointing the content root at a chosen directory.</summary>
public sealed class FakeHostEnvironment : IHostEnvironment
{
    public FakeHostEnvironment(string contentRoot) => ContentRootPath = contentRoot;

    public string EnvironmentName { get; set; } = "Testing";
    public string ApplicationName { get; set; } = "SqlAccess.Tests";
    public string ContentRootPath { get; set; }
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
        new Microsoft.Extensions.FileProviders.NullFileProvider();
}
