using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Networking;

/// <summary>
/// TCP front-end for the cache. Listens on the configured endpoint, accepts concurrent clients,
/// and runs a read-execute-reply loop per connection using the RESP protocol. Hosted service.
/// </summary>
public sealed class CacheTcpServer : BackgroundService
{
    private readonly CommandExecutor _executor;
    private readonly IConnectionManager _connections;
    private readonly ILogger<CacheTcpServer> _log;
    private readonly bool _enabled;
    private readonly IPAddress _bind;
    private readonly int _port;

    public CacheTcpServer(
        CommandExecutor executor, IConnectionManager connections,
        IOptions<CacheOptions> options, ILogger<CacheTcpServer> log)
    {
        _executor = executor;
        _connections = connections;
        _log = log;
        var o = options.Value;
        _enabled = o.TcpEnabled;
        _bind = IPAddress.TryParse(o.TcpBindAddress, out var ip) ? ip : IPAddress.Loopback;
        _port = o.TcpPort;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _log.LogInformation("Cache TCP server disabled (Cache:TcpEnabled=false).");
            return;
        }

        var listener = new TcpListener(_bind, _port);
        listener.Start();
        _log.LogInformation("Cache TCP server listening on {Bind}:{Port} (RESP).", _bind, _port);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken); // fire-and-forget per client
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        var remote = tcp.Client.RemoteEndPoint?.ToString() ?? "unknown";
        var conn = _connections.Register(remote);
        try
        {
            using (tcp)
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                var resp = new RespConnection(stream);

                while (!ct.IsCancellationRequested)
                {
                    string[]? args;
                    try
                    {
                        args = await resp.ReadCommandAsync(ct);
                    }
                    catch (FormatException fe)
                    {
                        await resp.WriteAsync(RespValue.Error("ERR Protocol error: " + fe.Message), ct);
                        break;
                    }

                    if (args is null) break;      // client disconnected
                    if (args.Length == 0) continue;

                    conn.IncrementCommands();
                    var reply = _executor.Execute(args);
                    await resp.WriteAsync(reply, ct);

                    if (args[0].Equals("QUIT", StringComparison.OrdinalIgnoreCase)) break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            // normal disconnect / shutdown
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Cache client {Remote} errored.", remote);
        }
        finally
        {
            _connections.Unregister(conn.Id);
        }
    }
}
