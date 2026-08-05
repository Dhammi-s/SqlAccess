using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Data;
using SqlAccess.Api.Models;

namespace SqlAccess.Api.Services;

public interface IAgencyService
{
    Task<List<AgencyListItem>> ListAsync(bool includeArchived, CancellationToken ct);
    Task<AgencyDetail?> GetAsync(int id, CancellationToken ct);
    Task<AgencyDetail> CreateAsync(CreateAgencyRequest req, CancellationToken ct);
    Task<AgencyDetail?> UpdateAsync(int id, UpdateAgencyRequest req, CancellationToken ct);
    Task<bool> ArchiveAsync(int id, bool archived, CancellationToken ct);
    Task<TestConnectionResult> TestAsync(int id, CancellationToken ct);
    Task<TestConnectionResult> TestAdHocAsync(string connectionString, CancellationToken ct);
    Task<DbRolesResult> GetRolesAsync(int id, CancellationToken ct);
    Task<CreateRoleResult> CreateRoleAsync(int id, CreateRoleRequest req, CancellationToken ct);
}

public sealed class AgencyService : IAgencyService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;

    public AgencyService(AppDbContext db, IEncryptionService enc)
    {
        _db = db;
        _enc = enc;
    }

    public async Task<List<AgencyListItem>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        var query = _db.Agencies.AsNoTracking().AsQueryable();
        if (!includeArchived)
            query = query.Where(a => !a.IsArchived);

        var rows = await query.OrderBy(a => a.AgencyName).ToListAsync(ct);
        return rows.Select(ToListItem).ToList();
    }

    public async Task<AgencyDetail?> GetAsync(int id, CancellationToken ct)
    {
        var a = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        return a is null ? null : ToDetail(a);
    }

    public async Task<AgencyDetail> CreateAsync(CreateAgencyRequest req, CancellationToken ct)
    {
        var conn = string.IsNullOrWhiteSpace(req.ConnectionString)
            ? BuildConnectionString(req.DbServer, req.DbName, req.DbUser, req.DbPassword)
            : req.ConnectionString;

        var entity = new Agency
        {
            AgencyName = req.AgencyName.Trim(),
            DomainUrl = req.DomainUrl,
            Location = req.Location,
            DbServer = req.DbServer,
            DbName = req.DbName,
            DbUser = req.DbUser,
            DbPassword = _enc.Encrypt(req.DbPassword),
            ConnectionString = _enc.Encrypt(conn),
            IsActive = req.IsActive,
            IsArchived = false,
            CreatedOn = DateTime.UtcNow,
            UpdatedOn = null,
        };

        _db.Agencies.Add(entity);
        await _db.SaveChangesAsync(ct);
        return ToDetail(entity);
    }

    public async Task<AgencyDetail?> UpdateAsync(int id, UpdateAgencyRequest req, CancellationToken ct)
    {
        var a = await _db.Agencies.FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        if (a is null) return null;

        a.AgencyName = req.AgencyName.Trim();
        a.DomainUrl = req.DomainUrl;
        a.Location = req.Location;
        a.DbServer = req.DbServer;
        a.DbName = req.DbName;
        a.DbUser = req.DbUser;

        // Keep existing password if the client did not supply a new one.
        var effectivePassword = string.IsNullOrEmpty(req.DbPassword)
            ? _enc.Decrypt(a.DbPassword)
            : req.DbPassword;

        if (!string.IsNullOrEmpty(req.DbPassword))
            a.DbPassword = _enc.Encrypt(req.DbPassword);

        var conn = string.IsNullOrWhiteSpace(req.ConnectionString)
            ? BuildConnectionString(req.DbServer, req.DbName, req.DbUser, effectivePassword)
            : req.ConnectionString;
        a.ConnectionString = _enc.Encrypt(conn);

        a.IsActive = req.IsActive;
        a.UpdatedOn = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return ToDetail(a);
    }

    public async Task<bool> ArchiveAsync(int id, bool archived, CancellationToken ct)
    {
        var a = await _db.Agencies.FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        if (a is null) return false;
        a.IsArchived = archived;
        a.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<TestConnectionResult> TestAsync(int id, CancellationToken ct)
    {
        var a = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        if (a is null) return new TestConnectionResult(false, "Agency not found.", 0);
        return await TestAdHocAsync(ResolveConnectionString(a), ct);
    }

    public async Task<TestConnectionResult> TestAdHocAsync(string connectionString, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                ConnectTimeout = 8,
            };
            await using var c = new SqlConnection(builder.ConnectionString);
            await c.OpenAsync(ct);
            sw.Stop();
            return new TestConnectionResult(true, $"Connected to {c.DataSource}/{c.Database}.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestConnectionResult(false, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    public async Task<DbRolesResult> GetRolesAsync(int id, CancellationToken ct)
    {
        var a = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        if (a is null) return new DbRolesResult(false, "Agency not found.", 0, new());

        const string sql = @"
SELECT dp.name,
       dp.is_fixed_role,
       dp.type_desc,
       (SELECT COUNT(*) FROM sys.database_role_members m WHERE m.role_principal_id = dp.principal_id) AS MemberCount,
       STUFF((SELECT ', ' + mp.name
              FROM sys.database_role_members drm
              JOIN sys.database_principals mp ON mp.principal_id = drm.member_principal_id
              WHERE drm.role_principal_id = dp.principal_id
              FOR XML PATH('')), 1, 2, '') AS Members
FROM sys.database_principals dp
WHERE dp.type = 'R'
ORDER BY dp.is_fixed_role DESC, dp.name;";

        try
        {
            await using var conn = new SqlConnection(OpenBuilder(ResolveConnectionString(a)));
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(sql, conn);
            var roles = new List<DbRole>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                roles.Add(new DbRole(
                    r.GetString(0),
                    r.GetBoolean(1),
                    r.GetString(2),
                    r.GetInt32(3),
                    await r.IsDBNullAsync(4, ct) ? null : r.GetString(4)));
            }
            return new DbRolesResult(true, $"{roles.Count} role(s) found.", roles.Count, roles);
        }
        catch (Exception ex)
        {
            return new DbRolesResult(false, ex.Message, 0, new());
        }
    }

    public async Task<CreateRoleResult> CreateRoleAsync(int id, CreateRoleRequest req, CancellationToken ct)
    {
        // Strict identifier validation. The name is ALSO bracket-escaped with QUOTENAME server-side,
        // and passed as a parameter to sp_executesql — defence in depth against SQL injection.
        var name = req.RoleName?.Trim() ?? string.Empty;
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]{0,127}$"))
            return new CreateRoleResult(false,
                "Invalid role name. Start with a letter or underscore; use only letters, numbers and underscores.");

        var a = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.AgencyId == id, ct);
        if (a is null) return new CreateRoleResult(false, "Agency not found.");

        try
        {
            await using var conn = new SqlConnection(OpenBuilder(ResolveConnectionString(a)));
            await conn.OpenAsync(ct);

            // Exists check
            await using (var check = new SqlCommand(
                "SELECT COUNT(*) FROM sys.database_principals WHERE name = @n AND type = 'R';", conn))
            {
                check.Parameters.AddWithValue("@n", name);
                var exists = (int)(await check.ExecuteScalarAsync(ct) ?? 0) > 0;
                if (exists) return new CreateRoleResult(false, $"Role '{name}' already exists.");
            }

            // CREATE ROLE [name]
            await using (var create = new SqlCommand(
                "DECLARE @sql nvarchar(max) = N'CREATE ROLE ' + QUOTENAME(@n); EXEC sp_executesql @sql;", conn))
            {
                create.Parameters.AddWithValue("@n", name);
                await create.ExecuteNonQueryAsync(ct);
            }

            if (req.ReadOnly)
            {
                // Database-wide read-only: grant SELECT to the role.
                await using var grant = new SqlCommand(
                    "DECLARE @sql nvarchar(max) = N'GRANT SELECT TO ' + QUOTENAME(@n); EXEC sp_executesql @sql;", conn);
                grant.Parameters.AddWithValue("@n", name);
                await grant.ExecuteNonQueryAsync(ct);
            }

            return new CreateRoleResult(true,
                req.ReadOnly
                    ? $"Read-only role '{name}' created and granted SELECT on the database."
                    : $"Role '{name}' created.");
        }
        catch (Exception ex)
        {
            return new CreateRoleResult(false, ex.Message);
        }
    }

    // ---------- helpers ----------

    /// <summary>Returns the effective connection string for an agency (decrypted, or built from parts).</summary>
    private string ResolveConnectionString(Agency a)
    {
        var conn = _enc.Decrypt(a.ConnectionString);
        if (string.IsNullOrWhiteSpace(conn))
            conn = BuildConnectionString(a.DbServer, a.DbName, a.DbUser, _enc.Decrypt(a.DbPassword));
        return conn!;
    }

    /// <summary>Applies a short connect timeout so admin queries fail fast on unreachable servers.</summary>
    private static string OpenBuilder(string connectionString)
        => new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 8 }.ConnectionString;

    private static string BuildConnectionString(string? server, string? db, string? user, string? password)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = server ?? string.Empty,
            InitialCatalog = db ?? string.Empty,
            UserID = user ?? string.Empty,
            Password = password ?? string.Empty,
            Encrypt = true,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true,
        };
        return b.ConnectionString;
    }

    private AgencyListItem ToListItem(Agency a) => new(
        a.AgencyId,
        a.AgencyName,
        a.DomainUrl,
        a.Location,
        a.DbServer,
        a.DbName,
        a.DbUser,
        Mask(_enc.Decrypt(a.DbPassword)),
        a.IsActive,
        a.IsArchived,
        a.CreatedOn,
        a.UpdatedOn);

    private AgencyDetail ToDetail(Agency a) => new(
        a.AgencyId,
        a.AgencyName,
        a.DomainUrl,
        a.Location,
        a.DbServer,
        a.DbName,
        a.DbUser,
        _enc.Decrypt(a.DbPassword),
        _enc.Decrypt(a.ConnectionString),
        a.IsActive,
        a.IsArchived,
        a.CreatedOn,
        a.UpdatedOn);

    private static string Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return string.Empty;
        return secret.Length <= 2 ? "••••" : $"{secret[0]}••••{secret[^1]}";
    }
}
