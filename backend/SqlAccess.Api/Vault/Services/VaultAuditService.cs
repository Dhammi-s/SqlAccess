using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Data;
using SqlAccess.Api.Vault.Models;

namespace SqlAccess.Api.Vault.Services;

public interface IVaultAuditService
{
    Task LogAsync(string action, bool success, int? appId = null, string? appName = null,
        int? secretId = null, string? secretName = null, string? detail = null, CancellationToken ct = default);

    Task<List<AuditLogItem>> ListAsync(int take, CancellationToken ct);
}

/// <summary>Writes an immutable audit trail (login, secret access, changes) with the caller's IP.</summary>
public sealed class VaultAuditService : IVaultAuditService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _http;

    public VaultAuditService(AppDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task LogAsync(string action, bool success, int? appId = null, string? appName = null,
        int? secretId = null, string? secretName = null, string? detail = null, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Success = success,
            ApplicationId = appId,
            ApplicationName = appName,
            SecretId = secretId,
            SecretName = secretName,
            Detail = detail,
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Timestamp = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<AuditLogItem>> ListAsync(int take, CancellationToken ct)
        => await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.AuditLogId)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditLogItem(a.AuditLogId, a.ApplicationId, a.ApplicationName, a.SecretId, a.SecretName,
                a.Action, a.Success, a.IpAddress, a.Detail, a.Timestamp))
            .ToListAsync(ct);
}
