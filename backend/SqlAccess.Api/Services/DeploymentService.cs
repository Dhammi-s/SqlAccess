using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.SqlServer.Dac;
using SqlAccess.Api.Data;
using SqlAccess.Api.Models;

namespace SqlAccess.Api.Services;

public interface IDeploymentService
{
    Task<DacpacInfo> SaveDacpacAsync(Stream content, string fileName, CancellationToken ct);
    Task<DeployResult> RunAsync(DeployRunRequest req, CancellationToken ct);
}

/// <summary>
/// Deploys a DACPAC to an agency's (tenant) database using DacFx — the managed engine behind SqlPackage.
/// Mirrors Deploy-AllTenants.ps1: builds the target connection from the agency's Db* columns,
/// supports Script (preview) and Publish (apply) with BlockOnPossibleDataLoss / DropObjectsNotInSource.
/// </summary>
public sealed class DeploymentService : IDeploymentService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly string _storageDir;
    private readonly ILogger<DeploymentService> _log;

    public DeploymentService(AppDbContext db, IEncryptionService enc, IHostEnvironment env, ILogger<DeploymentService> log)
    {
        _db = db;
        _enc = enc;
        _log = log;
        _storageDir = Path.Combine(env.ContentRootPath, "App_Data", "dacpacs");
        Directory.CreateDirectory(_storageDir);
    }
    #region "jasmeet singh"
    public async Task<DacpacInfo> SaveDacpacAsync(Stream content, string fileName, CancellationToken ct)
    {
        if (!fileName.EndsWith(".dacpac", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("File must be a .dacpac.");

        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(_storageDir, id + ".dacpac");

        await using (var fs = File.Create(path))
            await content.CopyToAsync(fs, ct);

        // Validate it's a real DACPAC before accepting it.
        try
        {
            using var pkg = DacPackage.Load(path);
            _ = pkg.Name;
        }
        catch (Exception ex)
        {
            File.Delete(path);
            throw new InvalidOperationException("The uploaded file is not a valid DACPAC: " + ex.Message);
        }

        var info = new FileInfo(path);
        return new DacpacInfo(id, Path.GetFileName(fileName), info.Length, DateTime.UtcNow);
    }
    #endregion
    public async Task<DeployResult> RunAsync(DeployRunRequest req, CancellationToken ct)
    {
        var a = await _db.Agencies.AsNoTracking().FirstOrDefaultAsync(x => x.AgencyId == req.AgencyId, ct);
        if (a is null)
            return Fail(req.AgencyId, "", "", "", "Agency not found.");

        var dacpacPath = Path.Combine(_storageDir, Path.GetFileName(req.DacpacId) + ".dacpac");
        if (!File.Exists(dacpacPath))
            return Fail(a.AgencyId, a.AgencyName, a.DbServer ?? "", a.DbName ?? "", "DACPAC not found. Upload it again.");

        var server = a.DbServer ?? "";
        var database = a.DbName ?? "";
        var targetConn = BuildTargetConnection(a);

        var sw = Stopwatch.StartNew();
        try
        {
            // DacFx is synchronous and CPU/IO heavy — run off the request thread.
            var result = await Task.Run(() =>
            {
                using var package = DacPackage.Load(dacpacPath);
                var services = new DacServices(targetConn);
                var log = new List<string>();
                services.Message += (_, e) => log.Add(e.Message.Message);

                var options = new DacDeployOptions
                {
                    BlockOnPossibleDataLoss = req.BlockOnPossibleDataLoss,
                    DropObjectsNotInSource = req.DropObjectsNotInSource,
                };

                if (req.GenerateScriptOnly)
                {
                    var script = services.GenerateDeployScript(package, database, options);
                    return (script: (string?)script, summary: "Script generated.");
                }

                services.Deploy(package, database, upgradeExisting: true, options, cancellationToken: ct);
                return (script: (string?)null, summary: string.Join(" | ", log.TakeLast(3)));
            }, ct);

            sw.Stop();
            return new DeployResult(
                a.AgencyId, a.AgencyName, server, database,
                Success: true,
                Message: req.GenerateScriptOnly ? "Script generated successfully." : "Deployment successful.",
                ElapsedMs: sw.ElapsedMilliseconds,
                ScriptGenerated: req.GenerateScriptOnly,
                Script: result.script);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogWarning(ex, "DACPAC {Mode} failed for agency {Id}",
                req.GenerateScriptOnly ? "script" : "deploy", a.AgencyId);
            var msg = ex is DacServicesException dse && dse.InnerException is not null
                ? $"{dse.Message}: {dse.InnerException.Message}"
                : ex.Message;
            return new DeployResult(a.AgencyId, a.AgencyName, server, database,
                false, msg, sw.ElapsedMilliseconds, false, null);
        }
    }

    private string BuildTargetConnection(Agency a)
    {
        // Same shape as Deploy-AllTenants.ps1: connect straight to the tenant DB with its own credentials.
        var b = new SqlConnectionStringBuilder
        {
            DataSource = a.DbServer ?? "",
            InitialCatalog = a.DbName ?? "",
            UserID = a.DbUser ?? "",
            Password = _enc.Decrypt(a.DbPassword) ?? "",
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = 15,
        };
        return b.ConnectionString;
    }

    private static DeployResult Fail(int id, string name, string server, string db, string message)
        => new(id, name, server, db, false, message, 0, false, null);
}
