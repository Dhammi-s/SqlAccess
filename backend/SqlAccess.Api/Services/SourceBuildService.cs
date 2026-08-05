using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.SqlServer.Dac;
using Microsoft.SqlServer.Dac.Model;
using SqlAccess.Api.Models;

namespace SqlAccess.Api.Services;

public interface ISourceBuildService
{
    Task<List<BranchInfo>> ListBranchesAsync(CancellationToken ct);
    Task<BuildResult> BuildFromBranchAsync(string branch, CancellationToken ct);
}

/// <summary>
/// Pulls the SQL project from GitHub and builds a DACPAC with DacFx — no SSDT/MSBuild needed.
/// Replaces the manual .dacpac upload: pick a branch, and the schema is built on the server.
/// </summary>
public sealed class SourceBuildService : ISourceBuildService
{
    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly string _storageDir;
    private readonly ILogger<SourceBuildService> _log;

    public SourceBuildService(IHttpClientFactory http, IConfiguration config, IHostEnvironment env, ILogger<SourceBuildService> log)
    {
        _http = http;
        _config = config;
        _log = log;
        _storageDir = Path.Combine(env.ContentRootPath, "App_Data", "dacpacs");
        Directory.CreateDirectory(_storageDir);
    }

    private string Repo => _config["GitHub:Repo"] ?? throw new InvalidOperationException("GitHub:Repo is not configured.");
    private string? Token => _config["GitHub:Token"];

    private HttpClient Client()
    {
        var c = _http.CreateClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SqlAccess.Api");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(Token))
            c.DefaultRequestHeaders.Authorization = new("Bearer", Token);
        c.Timeout = TimeSpan.FromMinutes(3);
        return c;
    }

    public async Task<List<BranchInfo>> ListBranchesAsync(CancellationToken ct)
    {
        using var c = Client();
        var url = $"https://api.github.com/repos/{Repo}/branches?per_page=100";
        using var resp = await c.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"GitHub API {(int)resp.StatusCode}: {body}");
        }

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return doc.RootElement.EnumerateArray()
            .Select(e => new BranchInfo(e.GetProperty("name").GetString() ?? ""))
            .Where(b => b.Name.Length > 0)
            .ToList();
    }

    public async Task<BuildResult> BuildFromBranchAsync(string branch, CancellationToken ct)
    {
        var work = Path.Combine(Path.GetTempPath(), "sqlaccess_build_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            // 1. Download the branch as a zip (public repos need no auth; token used if configured).
            var zipPath = Path.Combine(work, "src.zip");
            using (var c = Client())
            await using (var src = await c.GetStreamAsync(
                $"https://codeload.github.com/{Repo}/zip/refs/heads/{Uri.EscapeDataString(branch)}", ct))
            await using (var fs = File.Create(zipPath))
                await src.CopyToAsync(fs, ct);

            var extractDir = Path.Combine(work, "src");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // 2. Locate the .sqlproj (configured path, else first found).
            var configured = _config["GitHub:ProjectPath"];
            var projPath = !string.IsNullOrWhiteSpace(configured)
                ? Directory.EnumerateFiles(extractDir, Path.GetFileName(configured), SearchOption.AllDirectories).FirstOrDefault()
                : Directory.EnumerateFiles(extractDir, "*.sqlproj", SearchOption.AllDirectories).FirstOrDefault();

            if (projPath is null)
                return new BuildResult(false, "No .sqlproj found in the repository.", null, 0, 0);

            // 3. Build the DACPAC off the request thread (DacFx is synchronous & heavy).
            var id = Guid.NewGuid().ToString("N");
            var outPath = Path.Combine(_storageDir, id + ".dacpac");
            var (fileCount, warnings, errorText) = await Task.Run(() => BuildDacpac(projPath, outPath), ct);

            if (errorText is not null)
                return new BuildResult(false, errorText, null, fileCount, warnings);

            var info = new FileInfo(outPath);
            var dacpac = new DacpacInfo(id, $"WorkProvider360@{branch}.dacpac", info.Length, DateTime.UtcNow);
            return new BuildResult(true,
                $"Built from '{branch}': {fileCount} model files, {warnings} warning(s).", dacpac, fileCount, warnings);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Build from branch {Branch} failed", branch);
            return new BuildResult(false, ex.Message, null, 0, 0);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* best effort */ }
        }
    }

    private static (int fileCount, int warnings, string? error) BuildDacpac(string projPath, string outPath)
    {
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var projDir = Path.GetDirectoryName(Path.GetFullPath(projPath))!;
        var doc = XDocument.Load(projPath);

        string Rel(string p) => Path.GetFullPath(Path.Combine(projDir, p.Replace('\\', Path.DirectorySeparatorChar)));

        var buildFiles = doc.Descendants(ns + "Build")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Rel(v!))
            .Where(File.Exists)
            .ToList();

        if (buildFiles.Count == 0)
            return (0, 0, "The project has no build (.sql) files.");

        var dsp = doc.Descendants(ns + "DSP").FirstOrDefault()?.Value ?? "";
        var version = dsp.Contains("Sql170") ? SqlServerVersion.Sql170
            : dsp.Contains("Sql160") ? SqlServerVersion.Sql160
            : dsp.Contains("Sql150") ? SqlServerVersion.Sql150
            : SqlServerVersion.Sql160;

        using var model = new TSqlModel(version, new TSqlModelOptions());
        foreach (var f in buildFiles)
            model.AddObjects(File.ReadAllText(f));

        var messages = model.Validate();
        var errors = messages.Where(m => m.MessageType == DacMessageType.Error).ToList();
        if (errors.Count > 0)
            return (buildFiles.Count, messages.Count - errors.Count,
                "Schema build failed:\n" + string.Join("\n", errors.Take(10).Select(e => " • " + e)));

        DacPackageExtensions.BuildPackage(outPath, model, new PackageMetadata { Name = "WorkProvider360" });
        return (buildFiles.Count, messages.Count, null);
    }
}
