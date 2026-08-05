using Microsoft.EntityFrameworkCore;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Cicd.Providers;
using SqlAccess.Api.Data;
using SqlAccess.Api.Services;

namespace SqlAccess.Api.Cicd.Services;

public interface IWebsiteService
{
    Task<List<WebsiteListItem>> ListAsync(CancellationToken ct);
    Task<WebsiteDetail?> GetAsync(int id, CancellationToken ct);
    Task<WebsiteDetail> CreateAsync(UpsertWebsiteRequest req, CancellationToken ct);
    Task<WebsiteDetail?> UpdateAsync(int id, UpsertWebsiteRequest req, CancellationToken ct);
    Task<bool> DeleteAsync(int id, CancellationToken ct);
    Task<List<BranchInfo>> GetBranchesAsync(int id, CancellationToken ct);
    Task<List<BranchInfo>> PreviewBranchesAsync(string repoUrl, string? pat, CancellationToken ct);
    Task<CommitInfo?> GetLatestCommitAsync(int id, string branch, CancellationToken ct);
    Task<TestResult> TestGitAsync(TestGitRequest req, CancellationToken ct);
    Task<TestResult> TestFtpAsync(TestFtpRequest req, CancellationToken ct);
    IReadOnlyList<BuildTemplate> BuildTemplates();
}

public sealed class WebsiteService : IWebsiteService
{
    private readonly AppDbContext _db;
    private readonly IEncryptionService _enc;
    private readonly IGitHubService _github;
    private readonly IEnumerable<IDeploymentProvider> _providers;

    public WebsiteService(AppDbContext db, IEncryptionService enc, IGitHubService github, IEnumerable<IDeploymentProvider> providers)
    {
        _db = db;
        _enc = enc;
        _github = github;
        _providers = providers;
    }

    public async Task<List<WebsiteListItem>> ListAsync(CancellationToken ct)
    {
        var sites = await _db.Websites.AsNoTracking().OrderBy(w => w.WebsiteName).ToListAsync(ct);
        var ids = sites.Select(s => s.WebsiteId).ToList();

        var last = await _db.Deployments.AsNoTracking()
            .Where(d => ids.Contains(d.WebsiteId))
            .GroupBy(d => d.WebsiteId)
            .Select(g => g.OrderByDescending(x => x.DeploymentId).First())
            .ToListAsync(ct);
        var lastByWebsite = last.ToDictionary(d => d.WebsiteId);

        return sites.Select(s =>
        {
            lastByWebsite.TryGetValue(s.WebsiteId, out var d);
            return new WebsiteListItem(
                s.WebsiteId, s.WebsiteName, s.RepositoryUrl, s.GitProvider, s.DefaultBranch, s.ProjectType,
                s.FtpHost, s.IsActive, s.CreatedOn, s.UpdatedOn,
                d is null ? null : new DeploymentBrief(d.DeploymentId, d.Status, d.Branch, d.CommitId, d.StartedOn, d.FinishedOn));
        }).ToList();
    }

    public async Task<WebsiteDetail?> GetAsync(int id, CancellationToken ct)
    {
        var s = await _db.Websites.AsNoTracking().FirstOrDefaultAsync(w => w.WebsiteId == id, ct);
        return s is null ? null : ToDetail(s);
    }

    public async Task<WebsiteDetail> CreateAsync(UpsertWebsiteRequest req, CancellationToken ct)
    {
        var s = new Website { CreatedOn = DateTime.UtcNow };
        Apply(s, req, isCreate: true);
        _db.Websites.Add(s);
        await _db.SaveChangesAsync(ct);
        return ToDetail(s);
    }

    public async Task<WebsiteDetail?> UpdateAsync(int id, UpsertWebsiteRequest req, CancellationToken ct)
    {
        var s = await _db.Websites.FirstOrDefaultAsync(w => w.WebsiteId == id, ct);
        if (s is null) return null;
        Apply(s, req, isCreate: false);
        s.UpdatedOn = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return ToDetail(s);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct)
    {
        var s = await _db.Websites.FirstOrDefaultAsync(w => w.WebsiteId == id, ct);
        if (s is null) return false;
        // Remove dependent deployments + logs first (no cascade configured).
        var deps = await _db.Deployments.Where(d => d.WebsiteId == id).Select(d => d.DeploymentId).ToListAsync(ct);
        if (deps.Count > 0)
        {
            await _db.DeploymentLogs.Where(l => deps.Contains(l.DeploymentId)).ExecuteDeleteAsync(ct);
            await _db.Deployments.Where(d => d.WebsiteId == id).ExecuteDeleteAsync(ct);
        }
        _db.Websites.Remove(s);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<List<BranchInfo>> GetBranchesAsync(int id, CancellationToken ct)
    {
        var s = await _db.Websites.AsNoTracking().FirstOrDefaultAsync(w => w.WebsiteId == id, ct)
                ?? throw new InvalidOperationException("Website not found.");
        return await _github.GetBranchesAsync(s.RepositoryUrl ?? "", _enc.Decrypt(s.GitPat), ct);
    }

    public async Task<CommitInfo?> GetLatestCommitAsync(int id, string branch, CancellationToken ct)
    {
        var s = await _db.Websites.AsNoTracking().FirstOrDefaultAsync(w => w.WebsiteId == id, ct)
                ?? throw new InvalidOperationException("Website not found.");
        return await _github.GetLatestCommitAsync(s.RepositoryUrl ?? "", branch, _enc.Decrypt(s.GitPat), ct);
    }

    public Task<List<BranchInfo>> PreviewBranchesAsync(string repoUrl, string? pat, CancellationToken ct)
        => _github.GetBranchesAsync(repoUrl, pat, ct);

    public Task<TestResult> TestGitAsync(TestGitRequest req, CancellationToken ct)
        => _github.TestConnectionAsync(req.RepositoryUrl, req.Pat, ct);

    public async Task<TestResult> TestFtpAsync(TestFtpRequest req, CancellationToken ct)
    {
        var provider = _providers.First(p => p.Key == "FTP");
        var ctx = new DeployContext("", req.Host, req.Port, req.Username, req.Password, req.RootFolder);
        var r = await provider.TestAsync(ctx, ct);
        return new TestResult(r.Success, r.Message);
    }

    public IReadOnlyList<BuildTemplate> BuildTemplates() => new List<BuildTemplate>
    {
        new(ProjectType.AspNetCore, "dotnet restore && dotnet build -c Release", "dotnet publish -c Release -o publish", "publish"),
        new(ProjectType.ReactVite, "npm install", "npm run build", "dist"),
        new(ProjectType.Angular, "npm install", "npm run build -- --configuration production", "dist"),
        new(ProjectType.Node, "npm install", "npm run build", "dist"),
        new(ProjectType.Static, "", "", "."),
    };

    // ---------- mapping ----------

    private void Apply(Website s, UpsertWebsiteRequest req, bool isCreate)
    {
        s.WebsiteName = req.WebsiteName.Trim();
        s.RepositoryUrl = req.RepositoryUrl;
        s.GitProvider = req.GitProvider;
        s.DefaultBranch = req.DefaultBranch;
        s.ProjectType = req.ProjectType;
        s.BuildCommand = req.BuildCommand;
        s.PublishCommand = req.PublishCommand;
        s.PublishFolder = req.PublishFolder;
        s.FtpHost = req.FtpHost;
        s.FtpPort = req.FtpPort <= 0 ? 21 : req.FtpPort;
        s.FtpUsername = req.FtpUsername;
        s.FtpRootFolder = req.FtpRootFolder;
        s.IsActive = req.IsActive;

        // Secrets: encrypt only when supplied; on update, blank keeps the existing value.
        if (!string.IsNullOrEmpty(req.GitPat)) s.GitPat = _enc.Encrypt(req.GitPat);
        else if (isCreate) s.GitPat = null;

        if (!string.IsNullOrEmpty(req.FtpPassword)) s.FtpPassword = _enc.Encrypt(req.FtpPassword);
        else if (isCreate) s.FtpPassword = null;
    }

    private static WebsiteDetail ToDetail(Website s) => new(
        s.WebsiteId, s.WebsiteName, s.RepositoryUrl, s.GitProvider, s.DefaultBranch, s.ProjectType,
        s.BuildCommand, s.PublishCommand, s.PublishFolder,
        s.FtpHost, s.FtpPort, s.FtpUsername, s.FtpRootFolder,
        s.IsActive, !string.IsNullOrEmpty(s.GitPat), !string.IsNullOrEmpty(s.FtpPassword),
        s.CreatedOn, s.UpdatedOn);
}
