using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SqlAccess.Api.Data;
using SqlAccess.Api.Services;
using SqlAccess.Api.Vault.Models;

namespace SqlAccess.Api.Vault.Services;

public interface IVaultService
{
    // applications
    Task<RegisterAppResponse> RegisterApplicationAsync(RegisterAppRequest req, CancellationToken ct);
    Task<List<AppListItem>> ListApplicationsAsync(CancellationToken ct);
    Task<VaultTokenResponse?> AuthenticateAsync(VaultLoginRequest req, CancellationToken ct);

    // secrets (admin)
    Task<SecretListItem> CreateSecretAsync(CreateSecretRequest req, string? user, CancellationToken ct);
    Task<SecretListItem?> UpdateSecretAsync(int secretId, UpdateSecretRequest req, string? user, CancellationToken ct);
    Task<bool> DeleteSecretAsync(int secretId, CancellationToken ct);
    Task<List<SecretListItem>> ListSecretsAsync(string? search, CancellationToken ct);
    Task<SecretListItem?> RotateSecretAsync(RotateSecretRequest req, string? user, CancellationToken ct);
    Task<List<SecretVersionItem>> GetVersionsAsync(int secretId, CancellationToken ct);
    Task<SecretListItem?> RestoreVersionAsync(RestoreVersionRequest req, string? user, CancellationToken ct);

    // access
    Task<ApplicationSecretItem?> AssignSecretAsync(AssignSecretRequest req, CancellationToken ct);
    Task<bool> RevokeAsync(int applicationSecretId, CancellationToken ct);
    Task<List<ApplicationSecretItem>> ListAssignmentsAsync(CancellationToken ct);

    // retrieval (application)
    Task<SecretValueResponse?> GetSecretForApplicationAsync(int applicationId, string appName, string secretName, CancellationToken ct);
    Task<List<SecretValueResponse>> GetAllSecretsForApplicationAsync(int applicationId, string appName, CancellationToken ct);
}

public sealed class VaultService : IVaultService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly IVaultAuditService _audit;
    private readonly JwtSettings _jwt;

    public VaultService(AppDbContext db, IEncryptionService enc, IVaultAuditService audit, JwtSettings jwt)
    {
        _db = db;
        _enc = enc;
        _audit = audit;
        _jwt = jwt;
    }

    // ---------- Applications ----------

    public async Task<RegisterAppResponse> RegisterApplicationAsync(RegisterAppRequest req, CancellationToken ct)
    {
        var clientId = "app_" + Guid.NewGuid().ToString("N");
        var clientSecret = "sk_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "").Replace("/", "").Replace("=", "");

        var app = new VaultApplication
        {
            Name = req.Name.Trim(),
            ClientId = clientId,
            ClientSecretHash = BCrypt.Net.BCrypt.HashPassword(clientSecret),
            IsActive = true,
            CreatedOn = DateTime.UtcNow,
        };
        _db.VaultApplications.Add(app);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RegisterApp, true, app.ApplicationId, app.Name, detail: "Application registered", ct: ct);

        // ClientSecret returned ONCE — only the hash is stored.
        return new RegisterAppResponse(app.ApplicationId, app.Name, clientId, clientSecret);
    }

    public async Task<List<AppListItem>> ListApplicationsAsync(CancellationToken ct)
    {
        var apps = await _db.VaultApplications.AsNoTracking().OrderBy(a => a.Name).ToListAsync(ct);
        var counts = await _db.ApplicationSecrets.AsNoTracking()
            .GroupBy(x => x.ApplicationId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var map = counts.ToDictionary(x => x.Key, x => x.Count);
        return apps.Select(a => new AppListItem(a.ApplicationId, a.Name, a.ClientId, a.IsActive, a.CreatedOn,
            map.TryGetValue(a.ApplicationId, out var c) ? c : 0)).ToList();
    }

    public async Task<VaultTokenResponse?> AuthenticateAsync(VaultLoginRequest req, CancellationToken ct)
    {
        var app = await _db.VaultApplications.FirstOrDefaultAsync(a => a.ClientId == req.ClientId && a.IsActive, ct);
        var ok = app is not null && BCrypt.Net.BCrypt.Verify(req.ClientSecret, app.ClientSecretHash);
        if (!ok)
        {
            await _audit.LogAsync(AuditActions.AppLogin, false, app?.ApplicationId, app?.Name, detail: "Invalid client credentials", ct: ct);
            return null;
        }

        var (token, expires) = CreateAppToken(app!);
        await _audit.LogAsync(AuditActions.AppLogin, true, app!.ApplicationId, app.Name, detail: "Token issued", ct: ct);
        return new VaultTokenResponse(token, expires, app.Name);
    }

    private (string token, DateTime expires) CreateAppToken(VaultApplication app)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddMinutes(_jwt.ExpiryMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, app.ClientId),
            new Claim(ClaimTypes.Name, app.Name),
            new Claim("vault_app_id", app.ApplicationId.ToString()),
            new Claim(ClaimTypes.Role, "VaultApplication"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        var jwt = new JwtSecurityToken(_jwt.Issuer, _jwt.Audience, claims, expires: expires, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }

    // ---------- Secrets ----------

    public async Task<SecretListItem> CreateSecretAsync(CreateSecretRequest req, string? user, CancellationToken ct)
    {
        var name = req.Name.Trim();
        if (await _db.Secrets.AnyAsync(s => s.Name == name, ct))
            throw new InvalidOperationException($"A secret named '{name}' already exists.");

        var secret = new Secret
        {
            Name = name,
            SecretType = string.IsNullOrWhiteSpace(req.SecretType) ? "Custom" : req.SecretType,
            IsActive = true,
            CurrentVersion = 1,
            CreatedOn = DateTime.UtcNow,
        };
        _db.Secrets.Add(secret);
        await _db.SaveChangesAsync(ct);

        _db.SecretVersions.Add(new SecretVersion
        {
            SecretId = secret.SecretId,
            Version = 1,
            EncryptedValue = _enc.Encrypt(req.Value)!,
            IsCurrent = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = user,
        });
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.CreateSecret, true, secretId: secret.SecretId, secretName: name, ct: ct);
        return ToListItem(secret);
    }

    public async Task<SecretListItem?> UpdateSecretAsync(int secretId, UpdateSecretRequest req, string? user, CancellationToken ct)
    {
        var secret = await _db.Secrets.FirstOrDefaultAsync(s => s.SecretId == secretId, ct);
        if (secret is null) return null;

        if (!string.IsNullOrEmpty(req.Value))
            await AddVersionAsync(secret, req.Value, user, ct);
        if (!string.IsNullOrWhiteSpace(req.SecretType)) secret.SecretType = req.SecretType!;
        if (req.IsActive is not null) secret.IsActive = req.IsActive.Value;

        secret.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UpdateSecret, true, secretId: secret.SecretId, secretName: secret.Name, ct: ct);
        return ToListItem(secret);
    }

    public async Task<SecretListItem?> RotateSecretAsync(RotateSecretRequest req, string? user, CancellationToken ct)
    {
        var secret = await _db.Secrets.FirstOrDefaultAsync(s => s.SecretId == req.SecretId, ct);
        if (secret is null) return null;
        await AddVersionAsync(secret, req.NewValue, user, ct);
        secret.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RotateSecret, true, secretId: secret.SecretId, secretName: secret.Name,
            detail: $"Rotated to v{secret.CurrentVersion}", ct: ct);
        return ToListItem(secret);
    }

    public async Task<SecretListItem?> RestoreVersionAsync(RestoreVersionRequest req, string? user, CancellationToken ct)
    {
        var secret = await _db.Secrets.FirstOrDefaultAsync(s => s.SecretId == req.SecretId, ct);
        if (secret is null) return null;
        var target = await _db.SecretVersions.FirstOrDefaultAsync(v => v.SecretId == req.SecretId && v.Version == req.Version, ct);
        if (target is null) return null;

        // Restore = copy the old encrypted value forward as a new current version (history preserved).
        await AddVersionAsync(secret, _enc.Decrypt(target.EncryptedValue)!, user, ct);
        secret.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RestoreVersion, true, secretId: secret.SecretId, secretName: secret.Name,
            detail: $"Restored v{req.Version} as v{secret.CurrentVersion}", ct: ct);
        return ToListItem(secret);
    }

    private async Task AddVersionAsync(Secret secret, string value, string? user, CancellationToken ct)
    {
        await _db.SecretVersions.Where(v => v.SecretId == secret.SecretId && v.IsCurrent)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsCurrent, false), ct);
        var next = secret.CurrentVersion + 1;
        _db.SecretVersions.Add(new SecretVersion
        {
            SecretId = secret.SecretId,
            Version = next,
            EncryptedValue = _enc.Encrypt(value)!,
            IsCurrent = true,
            CreatedOn = DateTime.UtcNow,
            CreatedBy = user,
        });
        secret.CurrentVersion = next;
    }

    public async Task<bool> DeleteSecretAsync(int secretId, CancellationToken ct)
    {
        var secret = await _db.Secrets.FirstOrDefaultAsync(s => s.SecretId == secretId, ct);
        if (secret is null) return false;
        await _db.SecretVersions.Where(v => v.SecretId == secretId).ExecuteDeleteAsync(ct);
        await _db.ApplicationSecrets.Where(a => a.SecretId == secretId).ExecuteDeleteAsync(ct);
        _db.Secrets.Remove(secret);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.DeleteSecret, true, secretId: secretId, secretName: secret.Name, ct: ct);
        return true;
    }

    public async Task<List<SecretListItem>> ListSecretsAsync(string? search, CancellationToken ct)
    {
        var q = _db.Secrets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Name.Contains(s) || x.SecretType.Contains(s));
        }
        return await q.OrderBy(x => x.Name).Select(x => ToListItem(x)).ToListAsync(ct);
    }

    public async Task<List<SecretVersionItem>> GetVersionsAsync(int secretId, CancellationToken ct)
        => await _db.SecretVersions.AsNoTracking().Where(v => v.SecretId == secretId)
            .OrderByDescending(v => v.Version)
            .Select(v => new SecretVersionItem(v.SecretVersionId, v.Version, v.IsCurrent, v.CreatedOn, v.CreatedBy))
            .ToListAsync(ct);

    // ---------- Access ----------

    public async Task<ApplicationSecretItem?> AssignSecretAsync(AssignSecretRequest req, CancellationToken ct)
    {
        var app = await _db.VaultApplications.FirstOrDefaultAsync(a => a.ApplicationId == req.ApplicationId, ct);
        var secret = await _db.Secrets.FirstOrDefaultAsync(s => s.SecretId == req.SecretId, ct);
        if (app is null || secret is null) return null;

        var existing = await _db.ApplicationSecrets
            .FirstOrDefaultAsync(x => x.ApplicationId == req.ApplicationId && x.SecretId == req.SecretId, ct);
        if (existing is null)
        {
            existing = new ApplicationSecret { ApplicationId = req.ApplicationId, SecretId = req.SecretId, CreatedOn = DateTime.UtcNow };
            _db.ApplicationSecrets.Add(existing);
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(AuditActions.AssignSecret, true, app.ApplicationId, app.Name, secret.SecretId, secret.Name, ct: ct);
        }
        return new ApplicationSecretItem(existing.ApplicationSecretId, app.ApplicationId, app.Name, secret.SecretId, secret.Name, existing.CreatedOn);
    }

    public async Task<bool> RevokeAsync(int applicationSecretId, CancellationToken ct)
    {
        var link = await _db.ApplicationSecrets.FirstOrDefaultAsync(x => x.ApplicationSecretId == applicationSecretId, ct);
        if (link is null) return false;
        _db.ApplicationSecrets.Remove(link);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.RevokeSecret, true, link.ApplicationId, secretId: link.SecretId, ct: ct);
        return true;
    }

    public async Task<List<ApplicationSecretItem>> ListAssignmentsAsync(CancellationToken ct)
    {
        var q = from x in _db.ApplicationSecrets.AsNoTracking()
                join a in _db.VaultApplications.AsNoTracking() on x.ApplicationId equals a.ApplicationId
                join s in _db.Secrets.AsNoTracking() on x.SecretId equals s.SecretId
                orderby a.Name, s.Name
                select new ApplicationSecretItem(x.ApplicationSecretId, a.ApplicationId, a.Name, s.SecretId, s.Name, x.CreatedOn);
        return await q.ToListAsync(ct);
    }

    // ---------- Retrieval ----------

    public async Task<SecretValueResponse?> GetSecretForApplicationAsync(int applicationId, string appName, string secretName, CancellationToken ct)
    {
        var secret = await _db.Secrets.AsNoTracking().FirstOrDefaultAsync(s => s.Name == secretName && s.IsActive, ct);
        if (secret is null)
        {
            await _audit.LogAsync(AuditActions.GetSecret, false, applicationId, appName, secretName: secretName, detail: "Secret not found", ct: ct);
            return null;
        }

        var authorized = await _db.ApplicationSecrets.AsNoTracking()
            .AnyAsync(x => x.ApplicationId == applicationId && x.SecretId == secret.SecretId, ct);
        if (!authorized)
        {
            await _audit.LogAsync(AuditActions.GetSecret, false, applicationId, appName, secret.SecretId, secretName, "Not authorized", ct);
            return null;
        }

        var version = await _db.SecretVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.SecretId == secret.SecretId && v.IsCurrent, ct);
        if (version is null)
        {
            await _audit.LogAsync(AuditActions.GetSecret, false, applicationId, appName, secret.SecretId, secretName, "No current version", ct);
            return null;
        }

        await _audit.LogAsync(AuditActions.GetSecret, true, applicationId, appName, secret.SecretId, secretName, ct: ct);
        return new SecretValueResponse(secret.Name, secret.SecretType, _enc.Decrypt(version.EncryptedValue)!, version.Version);
    }

    /// <summary>All secrets assigned to the application (name + decrypted value) — used to hydrate app config at startup.</summary>
    public async Task<List<SecretValueResponse>> GetAllSecretsForApplicationAsync(int applicationId, string appName, CancellationToken ct)
    {
        var ids = await _db.ApplicationSecrets.AsNoTracking()
            .Where(x => x.ApplicationId == applicationId).Select(x => x.SecretId).ToListAsync(ct);
        var secrets = await _db.Secrets.AsNoTracking()
            .Where(s => ids.Contains(s.SecretId) && s.IsActive).ToListAsync(ct);

        var result = new List<SecretValueResponse>();
        foreach (var s in secrets)
        {
            var v = await _db.SecretVersions.AsNoTracking().FirstOrDefaultAsync(x => x.SecretId == s.SecretId && x.IsCurrent, ct);
            if (v is not null)
                result.Add(new SecretValueResponse(s.Name, s.SecretType, _enc.Decrypt(v.EncryptedValue)!, v.Version));
        }
        await _audit.LogAsync(AuditActions.GetSecret, true, applicationId, appName, detail: $"Bulk fetch: {result.Count} secret(s)", ct: ct);
        return result;
    }

    private static SecretListItem ToListItem(Secret s)
        => new(s.SecretId, s.Name, s.SecretType, s.IsActive, s.CurrentVersion, s.CreatedOn, s.UpdatedOn);
}
