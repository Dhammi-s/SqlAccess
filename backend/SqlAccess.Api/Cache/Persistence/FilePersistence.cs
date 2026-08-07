using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using SqlAccess.Api.Cache.Interfaces;
using SqlAccess.Api.Cache.Models;

namespace SqlAccess.Api.Cache.Persistence;

/// <summary>
/// Disk persistence: an append-only log (AOF) of every mutation plus periodic full snapshots.
/// Recovery loads the snapshot then replays the AOF (folded in memory so DEL/FLUSH apply cleanly),
/// then loads the resulting state into the store. Thread-safe.
///
/// Line format (space-separated; key/value are Base64 so they never contain spaces):
///   S &lt;b64key&gt; &lt;b64value&gt; &lt;expiryTicksUtc|-&gt;      SET
///   D &lt;b64key&gt;                                    DEL
///   E &lt;b64key&gt; &lt;expiryTicksUtc&gt;                  EXPIRE
///   F                                            FLUSH
/// </summary>
public sealed class FilePersistence : ICachePersistence
{
    private readonly ILogger<FilePersistence> _log;
    private readonly bool _aofEnabled;
    private readonly bool _snapshotEnabled;
    private readonly string _aofPath;
    private readonly string _snapshotPath;
    private readonly object _lock = new();
    private StreamWriter? _aof;

    public FilePersistence(IOptions<CacheOptions> options, IHostEnvironment env, ILogger<FilePersistence> log)
    {
        _log = log;
        var o = options.Value;
        var mode = (o.PersistenceMode ?? "Aof").Trim();
        _aofEnabled = mode.Equals("Aof", StringComparison.OrdinalIgnoreCase) || mode.Equals("Both", StringComparison.OrdinalIgnoreCase);
        _snapshotEnabled = mode.Equals("Snapshot", StringComparison.OrdinalIgnoreCase) || mode.Equals("Both", StringComparison.OrdinalIgnoreCase);

        var dir = Path.IsPathRooted(o.DataDirectory)
            ? o.DataDirectory
            : Path.Combine(env.ContentRootPath, o.DataDirectory);
        Directory.CreateDirectory(dir);
        _aofPath = Path.Combine(dir, "appendonly.aof");
        _snapshotPath = Path.Combine(dir, "snapshot.rdb");
    }

    // ---------- mutation hooks (append to AOF) ----------

    /// <inheritdoc />
    public void OnSet(string key, string value, DateTime? expiresAtUtc)
        => Append($"S {B64(key)} {B64(value)} {(expiresAtUtc.HasValue ? expiresAtUtc.Value.Ticks : "-")}");

    /// <inheritdoc />
    public void OnDelete(string key) => Append($"D {B64(key)}");

    /// <inheritdoc />
    public void OnExpire(string key, DateTime expiresAtUtc) => Append($"E {B64(key)} {expiresAtUtc.Ticks}");

    /// <inheritdoc />
    public void OnFlush() => Append("F");

    private void Append(string line)
    {
        if (!_aofEnabled) return;
        lock (_lock) _aof?.WriteLine(line);
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_lock) _aof?.Flush();
    }

    // ---------- snapshot ----------

    /// <inheritdoc />
    public Task SaveSnapshotAsync(ICacheStore store, CancellationToken ct)
    {
        if (!_snapshotEnabled) return Task.CompletedTask;

        lock (_lock)
        {
            var entries = store.Export().ToList(); // materialize under lock (see class docs for consistency reasoning)
            var tmp = _snapshotPath + ".tmp";
            using (var w = new StreamWriter(tmp, append: false, Encoding.UTF8))
            {
                foreach (var (key, value, exp) in entries)
                    w.WriteLine($"S {B64(key)} {B64(value)} {(exp.HasValue ? exp.Value.Ticks : "-")}");
            }
            File.Move(tmp, _snapshotPath, overwrite: true);

            // Snapshot now captures everything -> reset the AOF so it only holds post-snapshot writes.
            if (_aofEnabled)
            {
                _aof?.Dispose();
                _aof = new StreamWriter(new FileStream(_aofPath, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
            }
            _log.LogInformation("Cache snapshot saved ({Count} keys).", entries.Count);
        }
        return Task.CompletedTask;
    }

    // ---------- recovery ----------

    /// <inheritdoc />
    public void Recover(ICacheStore store)
    {
        // Fold snapshot + AOF into a working set so DEL/FLUSH apply correctly, then load the result.
        var working = new Dictionary<string, (string Value, DateTime? Exp)>(StringComparer.Ordinal);

        if (_snapshotEnabled && File.Exists(_snapshotPath))
            foreach (var line in File.ReadLines(_snapshotPath)) Apply(working, line);

        if (_aofEnabled && File.Exists(_aofPath))
            foreach (var line in File.ReadLines(_aofPath)) Apply(working, line);

        foreach (var (key, val) in working)
            store.LoadForRecovery(key, val.Value, val.Exp);

        if (working.Count > 0)
            _log.LogInformation("Cache recovered {Count} keys from disk.", working.Count);

        // Open the AOF for subsequent live writes (append mode preserves post-snapshot entries).
        if (_aofEnabled)
        {
            lock (_lock)
                _aof = new StreamWriter(new FileStream(_aofPath, FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        }
    }

    private static void Apply(Dictionary<string, (string, DateTime?)> working, string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        var parts = line.Split(' ');
        switch (parts[0])
        {
            case "S":
                working[UnB64(parts[1])] = (UnB64(parts[2]), parts[3] == "-" ? null : new DateTime(long.Parse(parts[3], CultureInfo.InvariantCulture), DateTimeKind.Utc));
                break;
            case "D":
                working.Remove(UnB64(parts[1]));
                break;
            case "E":
                var k = UnB64(parts[1]);
                if (working.TryGetValue(k, out var e))
                    working[k] = (e.Item1, new DateTime(long.Parse(parts[2], CultureInfo.InvariantCulture), DateTimeKind.Utc));
                break;
            case "F":
                working.Clear();
                break;
        }
    }

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));
    private static string UnB64(string s) => Encoding.UTF8.GetString(Convert.FromBase64String(s));
}
