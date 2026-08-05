using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SqlAccess.Api.Cicd.Models;

public static class DeploymentStatus
{
    public const string Queued = "Queued";
    public const string Running = "Running";
    public const string Success = "Success";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public static class LogType
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Error = "Error";
}

public static class ProjectType
{
    public const string AspNetCore = "AspNetCore";
    public const string ReactVite = "ReactVite";
    public const string Angular = "Angular";
    public const string Node = "Node";
    public const string Static = "Static";
}

[Table("Websites")]
public class Website
{
    [Key] public int WebsiteId { get; set; }
    public string WebsiteName { get; set; } = string.Empty;
    public string? RepositoryUrl { get; set; }
    public string GitProvider { get; set; } = "GitHub";
    public string? DefaultBranch { get; set; }
    public string ProjectType { get; set; } = Models.ProjectType.AspNetCore;

    /// <summary>Encrypted at rest.</summary>
    public string? GitPat { get; set; }

    public string? BuildCommand { get; set; }
    public string? PublishCommand { get; set; }
    public string? PublishFolder { get; set; }

    /// <summary>Deployment provider key: "FTP" or "SFTP".</summary>
    public string DeployProvider { get; set; } = "FTP";

    public string? FtpHost { get; set; }
    public int FtpPort { get; set; } = 21;
    public string? FtpUsername { get; set; }

    /// <summary>Encrypted at rest.</summary>
    public string? FtpPassword { get; set; }
    public string? FtpRootFolder { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

[Table("Deployments")]
public class Deployment
{
    [Key] public int DeploymentId { get; set; }
    public int WebsiteId { get; set; }
    public string? Branch { get; set; }
    public string? CommitId { get; set; }
    public string? CommitMessage { get; set; }
    public string? TriggeredBy { get; set; }
    public string Status { get; set; } = DeploymentStatus.Queued;
    public DateTime? StartedOn { get; set; }
    public DateTime? FinishedOn { get; set; }
    public DateTime CreatedOn { get; set; }
}

[Table("DeploymentLogs")]
public class DeploymentLog
{
    [Key] public long LogId { get; set; }
    public int DeploymentId { get; set; }
    public DateTime Timestamp { get; set; }
    public string LogType { get; set; } = Models.LogType.Info;
    public string? Message { get; set; }
}
