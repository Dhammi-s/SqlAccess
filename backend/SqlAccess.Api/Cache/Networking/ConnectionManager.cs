using System.Collections.Concurrent;

namespace SqlAccess.Api.Cache.Networking;

/// <summary>State for one connected TCP client. Command counts feed the monitoring phase.</summary>
public sealed class ClientConnection
{
    public required string Id { get; init; }
    public required string RemoteEndpoint { get; init; }
    public DateTime ConnectedAtUtc { get; init; } = DateTime.UtcNow;

    private long _commands;
    public long CommandsProcessed => Interlocked.Read(ref _commands);
    public void IncrementCommands() => Interlocked.Increment(ref _commands);
}

/// <summary>Tracks live client connections. Thread-safe; registered as a singleton.</summary>
public interface IConnectionManager
{
    ClientConnection Register(string remoteEndpoint);
    void Unregister(string id);
    int Count { get; }
    IReadOnlyCollection<ClientConnection> Snapshot();
}

/// <inheritdoc />
public sealed class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, ClientConnection> _clients = new();
    private long _nextId;

    /// <inheritdoc />
    public ClientConnection Register(string remoteEndpoint)
    {
        var id = Interlocked.Increment(ref _nextId).ToString();
        var conn = new ClientConnection { Id = id, RemoteEndpoint = remoteEndpoint };
        _clients[id] = conn;
        return conn;
    }

    /// <inheritdoc />
    public void Unregister(string id) => _clients.TryRemove(id, out _);

    /// <inheritdoc />
    public int Count => _clients.Count;

    /// <inheritdoc />
    public IReadOnlyCollection<ClientConnection> Snapshot() => _clients.Values.ToArray();
}
