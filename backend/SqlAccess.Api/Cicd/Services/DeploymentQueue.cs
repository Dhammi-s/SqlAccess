using System.Threading.Channels;

namespace SqlAccess.Api.Cicd.Services;

public interface IDeploymentQueue
{
    ValueTask EnqueueAsync(int deploymentId, CancellationToken ct = default);
    ValueTask<int> DequeueAsync(CancellationToken ct);
    /// <summary>Requests cancellation of a running/queued deployment.</summary>
    void RequestCancel(int deploymentId);
    bool IsCancelRequested(int deploymentId);
    void ClearCancel(int deploymentId);
}

public sealed class DeploymentQueue : IDeploymentQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    private readonly HashSet<int> _cancelled = new();
    private readonly object _lock = new();

    public ValueTask EnqueueAsync(int deploymentId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(deploymentId, ct);

    public ValueTask<int> DequeueAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);

    public void RequestCancel(int deploymentId)
    {
        lock (_lock) _cancelled.Add(deploymentId);
    }

    public bool IsCancelRequested(int deploymentId)
    {
        lock (_lock) return _cancelled.Contains(deploymentId);
    }

    public void ClearCancel(int deploymentId)
    {
        lock (_lock) _cancelled.Remove(deploymentId);
    }
}
