using System.ComponentModel.DataAnnotations;

namespace SqlAccess.Api.Vault.Models;

// ---------- Application auth (machine-to-machine) ----------

public record VaultLoginRequest([Required] string ClientId, [Required] string ClientSecret);
public record VaultTokenResponse(string Token, DateTime ExpiresAt, string ApplicationName);

// ---------- Applications (admin) ----------

public record RegisterAppRequest([Required, MaxLength(200)] string Name);
/// <summary>ClientSecret is returned ONCE at creation — it is never stored or shown again.</summary>
public record RegisterAppResponse(int ApplicationId, string Name, string ClientId, string ClientSecret);

public record AppListItem(int ApplicationId, string Name, string ClientId, bool IsActive, DateTime CreatedOn, int SecretCount);

// ---------- Secrets (admin) ----------

public class CreateSecretRequest
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public string SecretType { get; set; } = "Custom";
    [Required] public string Value { get; set; } = string.Empty;
}

public class UpdateSecretRequest
{
    /// <summary>New value — stored as a new version. Leave null to only change metadata.</summary>
    public string? Value { get; set; }
    public string? SecretType { get; set; }
    public bool? IsActive { get; set; }
}

public record RotateSecretRequest([Required] int SecretId, [Required] string NewValue);
public record RestoreVersionRequest([Required] int SecretId, [Required] int Version);

public record SecretListItem(
    int SecretId, string Name, string SecretType, bool IsActive, int CurrentVersion, DateTime CreatedOn, DateTime? UpdatedOn);

/// <summary>Decrypted value — only returned to an authorized application, or to admin on explicit reveal.</summary>
public record SecretValueResponse(string Name, string SecretType, string Value, int Version);

public record SecretVersionItem(int SecretVersionId, int Version, bool IsCurrent, DateTime CreatedOn, string? CreatedBy);

// ---------- Access assignment (admin) ----------

public record AssignSecretRequest([Required] int ApplicationId, [Required] int SecretId);
public record ApplicationSecretItem(int ApplicationSecretId, int ApplicationId, string ApplicationName, int SecretId, string SecretName, DateTime CreatedOn);

// ---------- Audit ----------

public record AuditLogItem(
    long AuditLogId, int? ApplicationId, string? ApplicationName, int? SecretId, string? SecretName,
    string Action, bool Success, string? IpAddress, string? Detail, DateTime Timestamp);
