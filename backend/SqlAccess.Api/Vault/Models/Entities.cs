using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SqlAccess.Api.Vault.Models;

/// <summary>Well-known secret categories (free-form "Custom" also allowed).</summary>
public static class SecretTypes
{
    public static readonly string[] All =
    {
        "ConnectionString", "JwtSecret", "Smtp", "OpenAI", "Anthropic",
        "Twilio", "Stripe", "Google", "Firebase", "Custom",
    };
}

public static class AuditActions
{
    public const string AppLogin = "AppLogin";
    public const string GetSecret = "GetSecret";
    public const string CreateSecret = "CreateSecret";
    public const string UpdateSecret = "UpdateSecret";
    public const string DeleteSecret = "DeleteSecret";
    public const string RotateSecret = "RotateSecret";
    public const string RestoreVersion = "RestoreVersion";
    public const string RegisterApp = "RegisterApplication";
    public const string AssignSecret = "AssignSecret";
    public const string RevokeSecret = "RevokeSecret";
}

/// <summary>A client application that can authenticate (ClientId + ClientSecret) and read assigned secrets.</summary>
[Table("Applications")]
public class VaultApplication
{
    [Key] public int ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    /// <summary>BCrypt hash of the client secret — never the plaintext.</summary>
    public string ClientSecretHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedOn { get; set; }
}

[Table("Secrets")]
public class Secret
{
    [Key] public int SecretId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SecretType { get; set; } = "Custom";
    public bool IsActive { get; set; } = true;
    public int CurrentVersion { get; set; }
    public DateTime CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

[Table("SecretVersions")]
public class SecretVersion
{
    [Key] public int SecretVersionId { get; set; }
    public int SecretId { get; set; }
    public int Version { get; set; }
    /// <summary>AES-256-GCM encrypted value (via IEncryptionService).</summary>
    public string EncryptedValue { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public DateTime CreatedOn { get; set; }
    public string? CreatedBy { get; set; }
}

[Table("ApplicationSecrets")]
public class ApplicationSecret
{
    [Key] public int ApplicationSecretId { get; set; }
    public int ApplicationId { get; set; }
    public int SecretId { get; set; }
    public DateTime CreatedOn { get; set; }
}

[Table("AuditLogs")]
public class AuditLog
{
    [Key] public long AuditLogId { get; set; }
    public int? ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public int? SecretId { get; set; }
    public string? SecretName { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? Detail { get; set; }
    public DateTime Timestamp { get; set; }
}
