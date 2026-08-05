using System.ComponentModel.DataAnnotations;

namespace SqlAccess.Api.Cicd.Models;

// ---------- Websites ----------

public record WebsiteListItem(
    int WebsiteId, string WebsiteName, string? RepositoryUrl, string GitProvider,
    string? DefaultBranch, string ProjectType, string? FtpHost, bool IsActive,
    DateTime CreatedOn, DateTime? UpdatedOn,
    DeploymentBrief? LastDeployment);

/// <summary>Full website config for editing. Secrets are NEVER returned — booleans flag whether they are set.</summary>
public record WebsiteDetail(
    int WebsiteId, string WebsiteName, string? RepositoryUrl, string GitProvider,
    string? DefaultBranch, string ProjectType,
    string? BuildCommand, string? PublishCommand, string? PublishFolder,
    string DeployProvider, string? FtpHost, int FtpPort, string? FtpUsername, string? FtpRootFolder,
    bool IsActive, bool HasGitPat, bool HasFtpPassword,
    DateTime CreatedOn, DateTime? UpdatedOn);

public class UpsertWebsiteRequest
{
    [Required, MaxLength(200)] public string WebsiteName { get; set; } = string.Empty;
    public string? RepositoryUrl { get; set; }
    public string GitProvider { get; set; } = "GitHub";
    public string? DefaultBranch { get; set; }
    public string ProjectType { get; set; } = Models.ProjectType.AspNetCore;

    /// <summary>Leave null/empty to keep the existing token on update.</summary>
    public string? GitPat { get; set; }

    public string? BuildCommand { get; set; }
    public string? PublishCommand { get; set; }
    public string? PublishFolder { get; set; }

    public string DeployProvider { get; set; } = "FTP";
    public string? FtpHost { get; set; }
    public int FtpPort { get; set; } = 21;
    public string? FtpUsername { get; set; }
    /// <summary>Leave null/empty to keep the existing password on update.</summary>
    public string? FtpPassword { get; set; }
    public string? FtpRootFolder { get; set; }

    public bool IsActive { get; set; } = true;
}

// ---------- Git / connection tests ----------

public record TestGitRequest(string RepositoryUrl, string? Pat);
public record TestResult(bool Success, string Message);

public record TestFtpRequest(string Host, int Port, string Username, string? Password, string? RootFolder, string? Provider);

public record BranchInfo(string Name);
public record CommitInfo(string Sha, string ShortSha, string Message, string Author, DateTime? Date);

/// <summary>Suggested build/publish commands for a project type.</summary>
public record BuildTemplate(string ProjectType, string BuildCommand, string PublishCommand, string PublishFolder);

// ---------- Deployments ----------

public record TriggerDeployRequest(int WebsiteId, string Branch);

public record DeploymentBrief(int DeploymentId, string Status, string? Branch, string? CommitId, DateTime? StartedOn, DateTime? FinishedOn);

public record DeploymentListItem(
    int DeploymentId, int WebsiteId, string? WebsiteName, string? Branch, string? CommitId,
    string? CommitMessage, string? TriggeredBy, string Status,
    DateTime? StartedOn, DateTime? FinishedOn, double? DurationSeconds);

public record LogEntry(long LogId, DateTime Timestamp, string LogType, string? Message);
