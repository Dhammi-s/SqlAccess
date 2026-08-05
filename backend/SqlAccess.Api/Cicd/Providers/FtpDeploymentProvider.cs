using System.Text;
using FluentFTP;

namespace SqlAccess.Api.Cicd.Providers;

/// <summary>Recursive FTP upload with mirror semantics (create dirs, overwrite changed, delete removed).</summary>
public sealed class FtpDeploymentProvider : IDeploymentProvider
{
    public string Key => "FTP";

    private static AsyncFtpClient CreateClient(DeployContext ctx)
    {
        var client = new AsyncFtpClient(ctx.FtpHost, ctx.FtpUsername, ctx.FtpPassword ?? "", ctx.FtpPort <= 0 ? 21 : ctx.FtpPort);
        client.Config.EncryptionMode = FtpEncryptionMode.Auto;
        client.Config.ValidateAnyCertificate = true;
        client.Config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Config.ConnectTimeout = 15000;
        client.Config.RetryAttempts = 3;
        return client;
    }

    public async Task<ProviderTestResult> TestAsync(DeployContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ctx.FtpHost))
            return new ProviderTestResult(false, "FTP host is required.");
        try
        {
            await using var client = CreateClient(ctx);
            await client.Connect(ct);

            var root = string.IsNullOrWhiteSpace(ctx.FtpRootFolder) ? "/" : ctx.FtpRootFolder!;
            await client.CreateDirectory(root, ct);

            var probe = root.TrimEnd('/') + "/.sqlaccess-test.txt";
            var bytes = Encoding.UTF8.GetBytes("SQL Access CI/CD FTP test " + DateTime.UtcNow.ToString("O"));
            await client.UploadBytes(bytes, probe, FtpRemoteExists.Overwrite, false, token: ct);
            await client.DeleteFile(probe, ct);

            return new ProviderTestResult(true, $"Connected to {ctx.FtpHost} and wrote to {root}.");
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
            throw new InvalidOperationException("FTP host is not configured.");
        if (!Directory.Exists(ctx.LocalFolder))
            throw new InvalidOperationException($"Publish folder not found: {ctx.LocalFolder}");

        var remote = string.IsNullOrWhiteSpace(ctx.FtpRootFolder) ? "/" : ctx.FtpRootFolder!;

        await log("Info", $"Connecting to FTP {ctx.FtpHost}:{(ctx.FtpPort <= 0 ? 21 : ctx.FtpPort)}...");
        await using var client = CreateClient(ctx);
        await client.Connect(ct);
        await log("Info", $"Connected. Uploading to {remote} (mirror)...");

        var reporter = new Progress<FtpProgress>(p =>
        {
            var pct = (int)Math.Clamp(p.Progress, 0, 100);
            _ = progress(pct, p.RemotePath);
        });

        var results = await client.UploadDirectory(
            ctx.LocalFolder, remote,
            FtpFolderSyncMode.Mirror,
            FtpRemoteExists.Overwrite,
            FtpVerify.None,
            null,
            reporter,
            ct);

        var uploaded = results.Count(r => r.IsSuccess && r.Type == FtpObjectType.File);
        var failed = results.Count(r => r.IsFailed);
        await progress(100, null);
        await log(failed > 0 ? "Warning" : "Info",
            $"Upload complete: {uploaded} file(s) uploaded, {failed} failed.");

        if (failed > 0)
            throw new InvalidOperationException($"{failed} file(s) failed to upload.");
    }
}
