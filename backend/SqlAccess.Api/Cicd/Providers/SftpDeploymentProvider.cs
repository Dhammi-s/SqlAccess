using System.Text;
using Renci.SshNet;

namespace SqlAccess.Api.Cicd.Providers;

/// <summary>
/// SFTP upload over SSH (port 22). Works through firewalls that block FTP (port 21).
/// Same DeployContext fields as FTP: host/port/username/password/rootFolder.
/// </summary>
public sealed class SftpDeploymentProvider : IDeploymentProvider
{
    public string Key => "SFTP";

    private static SftpClient Create(DeployContext ctx)
    {
        var port = ctx.FtpPort <= 0 ? 22 : ctx.FtpPort;
        var client = new SftpClient(ctx.FtpHost, port, ctx.FtpUsername, ctx.FtpPassword ?? "");
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    private static string NormalizeRoot(string? root)
    {
        var r = (root ?? "/").Replace('\\', '/').Trim();
        if (!r.StartsWith('/')) r = "/" + r;
        return r.TrimEnd('/') is { Length: > 0 } t ? t : "/";
    }

    public async Task<ProviderTestResult> TestAsync(DeployContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.FtpHost))
            return new ProviderTestResult(false, "Host is required.");
        try
        {
            return await Task.Run(() =>
            {
                using var client = Create(ctx);
                client.Connect();
                var root = NormalizeRoot(ctx.FtpRootFolder);
                EnsureDir(client, root);
                var probe = (root == "/" ? "" : root) + "/.sqlaccess-test.txt";
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes("SQL Access CI/CD SFTP test")))
                    client.UploadFile(ms, probe, true);
                client.DeleteFile(probe);
                client.Disconnect();
                return new ProviderTestResult(true, $"Connected to {ctx.FtpHost} over SFTP and wrote to {root}.");
            }, ct);
        }
        catch (Exception ex)
        {
            return new ProviderTestResult(false, ex.Message);
        }
    }

    public async Task DeployAsync(
        DeployContext ctx, Func<string, string, Task> log, Func<int, string?, Task> progress, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.FtpHost))
            throw new InvalidOperationException("SFTP host is not configured.");
        if (!Directory.Exists(ctx.LocalFolder))
            throw new InvalidOperationException($"Publish folder not found: {ctx.LocalFolder}");

        var root = NormalizeRoot(ctx.FtpRootFolder);
        var files = Directory.GetFiles(ctx.LocalFolder, "*", SearchOption.AllDirectories);
        await log("Info", $"Connecting to {ctx.FtpHost}:{(ctx.FtpPort <= 0 ? 22 : ctx.FtpPort)} over SFTP...");

        await Task.Run(async () =>
        {
            using var client = Create(ctx);
            client.Connect();
            await log("Info", $"Connected. Uploading {files.Length} file(s) to {root}...");
            EnsureDir(client, root);

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            var knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var done = 0;
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(ctx.LocalFolder, file).Replace('\\', '/');
                var remote = (root == "/" ? "" : root) + "/" + rel;

                var dir = remote[..remote.LastIndexOf('/')];
                if (dir.Length == 0) dir = "/";
                EnsureDir(client, dir, knownDirs);
                AddParents(keep, root, remote);

                UploadWithRetry(client, file, remote, ct);

                keep.Add(remote);
                done++;
                await progress((int)(done * 100.0 / Math.Max(1, files.Length)), rel);
            }

            // Mirror: delete remote files no longer in source (best-effort).
            try
            {
                MirrorDelete(client, root, keep);
            }
            catch (Exception ex)
            {
                await log("Warning", "Mirror cleanup skipped: " + ex.Message);
            }

            client.Disconnect();
            await progress(100, null);
            await log("Info", $"SFTP upload complete: {done} file(s).");
        }, ct);
    }

    private static void UploadWithRetry(SftpClient client, string localFile, string remote, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                // FileShare.ReadWrite so we can read files the build server still holds a handle on.
                using var fs = new FileStream(localFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                client.UploadFile(fs, remote, true);
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(600); // transient lock (build server / AV) — back off and retry
            }
        }
        throw new IOException($"Could not read '{Path.GetFileName(localFile)}' after retries: {last?.Message}", last);
    }

    private static void EnsureDir(SftpClient client, string dir, HashSet<string>? known = null)
    {
        var parts = dir.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = "";
        foreach (var p in parts)
        {
            cur += "/" + p;
            if (known is not null && known.Contains(cur)) continue; // already ensured this run
            if (!client.Exists(cur)) client.CreateDirectory(cur);
            known?.Add(cur);
        }
    }

    private static void AddParents(HashSet<string> keep, string root, string remotePath)
    {
        var idx = remotePath.LastIndexOf('/');
        while (idx > 0)
        {
            var d = remotePath[..idx];
            if (d.Length < root.Length) break;
            keep.Add(d);
            idx = d.LastIndexOf('/');
        }
    }

    private static void MirrorDelete(SftpClient client, string dir, HashSet<string> keep)
    {
        foreach (var entry in client.ListDirectory(dir))
        {
            if (entry.Name is "." or "..") continue;
            var full = entry.FullName;
            if (entry.IsDirectory)
            {
                MirrorDelete(client, full, keep);
                if (!keep.Contains(full) && !client.ListDirectory(full).Any(f => f.Name is not ("." or "..")))
                    client.DeleteDirectory(full);
            }
            else if (entry.IsRegularFile && !keep.Contains(full))
            {
                client.DeleteFile(full);
            }
        }
    }
}
