using System.ComponentModel.DataAnnotations;

namespace SqlAccess.Api.Models;

// ---------- Auth ----------

public record LoginRequest(
    [Required] string Username,
    [Required] string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string Username);

// ---------- Agencies ----------

/// <summary>List item — secrets are masked, never sent in plaintext.</summary>
public record AgencyListItem(
    int AgencyId,
    string AgencyName,
    string? DomainUrl,
    string? Location,
    string? DbServer,
    string? DbName,
    string? DbUser,
    string PasswordMasked,
    bool IsActive,
    bool IsArchived,
    DateTime CreatedOn,
    DateTime? UpdatedOn);

/// <summary>Full detail used for editing — secrets ARE decrypted (behind auth).</summary>
public record AgencyDetail(
    int AgencyId,
    string AgencyName,
    string? DomainUrl,
    string? Location,
    string? DbServer,
    string? DbName,
    string? DbUser,
    string? DbPassword,
    string? ConnectionString,
    bool IsActive,
    bool IsArchived,
    DateTime CreatedOn,
    DateTime? UpdatedOn);

public class CreateAgencyRequest
{
    [Required, MaxLength(200)]
    public string AgencyName { get; set; } = string.Empty;
    public string? DomainUrl { get; set; }
    public string? Location { get; set; }
    public string? DbServer { get; set; }
    public string? DbName { get; set; }
    public string? DbUser { get; set; }
    public string? DbPassword { get; set; }
    /// <summary>Optional. If omitted, it is auto-built from the parts above.</summary>
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UpdateAgencyRequest
{
    [Required, MaxLength(200)]
    public string AgencyName { get; set; } = string.Empty;
    public string? DomainUrl { get; set; }
    public string? Location { get; set; }
    public string? DbServer { get; set; }
    public string? DbName { get; set; }
    public string? DbUser { get; set; }
    /// <summary>Leave null/empty to KEEP the existing stored password.</summary>
    public string? DbPassword { get; set; }
    /// <summary>Leave null/empty to auto-rebuild or keep existing.</summary>
    public string? ConnectionString { get; set; }
    public bool IsActive { get; set; } = true;
}

public record TestConnectionResult(bool Success, string Message, long ElapsedMs);

// ---------- Database security / roles ----------

public record DbRole(string RoleName, bool IsFixedRole, string TypeDesc, int MemberCount, string? Members);

public record DbRolesResult(bool Success, string Message, int TotalRoles, List<DbRole> Roles);

public class CreateRoleRequest
{
    [Required, MaxLength(128)]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>When true, the new role is granted database-wide SELECT (read-only).</summary>
    public bool ReadOnly { get; set; }
}

public record CreateRoleResult(bool Success, string Message);

// ---------- DACPAC deployment ----------

public record DacpacInfo(string DacpacId, string FileName, long SizeBytes, DateTime UploadedOn);

public record BranchInfo(string Name);

public class BuildFromBranchRequest
{
    [Required]
    public string Branch { get; set; } = string.Empty;
}

public record BuildResult(
    bool Success,
    string Message,
    DacpacInfo? Dacpac,
    int ModelFileCount,
    int Warnings,
    List<string> Errors,
    bool EmailSent);

public class DeployRunRequest
{
    [Required]
    public string DacpacId { get; set; } = string.Empty;

    [Required]
    public int AgencyId { get; set; }

    /// <summary>When true, only generates the deployment script (no changes applied).</summary>
    public bool GenerateScriptOnly { get; set; } = true;

    /// <summary>Mirrors sqlpackage /p:BlockOnPossibleDataLoss.</summary>
    public bool BlockOnPossibleDataLoss { get; set; } = false;

    /// <summary>Mirrors sqlpackage /p:DropObjectsNotInSource.</summary>
    public bool DropObjectsNotInSource { get; set; } = false;
}

public record DeployResult(
    int AgencyId,
    string AgencyName,
    string TargetServer,
    string TargetDatabase,
    bool Success,
    string Message,
    long ElapsedMs,
    bool ScriptGenerated,
    string? Script);
