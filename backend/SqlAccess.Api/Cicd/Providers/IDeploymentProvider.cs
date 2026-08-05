namespace SqlAccess.Api.Cicd.Providers;

/// <summary>Everything a provider needs to deploy. Extend with new fields as providers are added (SFTP, Azure, S3…).</summary>
public sealed record DeployContext(
    string LocalFolder,
    string? FtpHost,
    int FtpPort,
    string? FtpUsername,
    string? FtpPassword,
    string? FtpRootFolder);

public sealed record ProviderTestResult(bool Success, string Message);

/// <summary>
/// Strategy for pushing a built folder to a target. Register one implementation per key
/// ("FTP", "SFTP", "AzureAppService", …); the orchestrator selects by key.
/// </summary>
public interface IDeploymentProvider
{
    /// <summary>Unique key, e.g. "FTP".</summary>
    string Key { get; }

    Task<ProviderTestResult> TestAsync(DeployContext ctx, CancellationToken ct);

    Task DeployAsync(
        DeployContext ctx,
        Func<string, string, Task> log,
        Func<int, string?, Task> progress,
        CancellationToken ct);
}
