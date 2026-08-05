using System.Text.Json;
using System.Text.RegularExpressions;
using SqlAccess.Api.Cicd.Models;

namespace SqlAccess.Api.Cicd.Services;

public interface IGitHubService
{
    Task<List<BranchInfo>> GetBranchesAsync(string repoUrl, string? pat, CancellationToken ct);
    Task<CommitInfo?> GetLatestCommitAsync(string repoUrl, string branch, string? pat, CancellationToken ct);
    Task<TestResult> TestConnectionAsync(string repoUrl, string? pat, CancellationToken ct);
}

/// <summary>Talks to the GitHub REST API for a given repo URL (public or private via PAT).</summary>
public sealed class GitHubService : IGitHubService
{
    private readonly IHttpClientFactory _http;
    public GitHubService(IHttpClientFactory http) => _http = http;

    public static (string owner, string repo)? ParseRepo(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl)) return null;
        var m = Regex.Match(repoUrl.Trim(),
            @"github\.com[/:]([^/]+)/([^/.]+)(?:\.git)?/?$", RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    private HttpClient Client(string? pat)
    {
        var c = _http.CreateClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("SqlAccess.Cicd");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(pat))
            c.DefaultRequestHeaders.Authorization = new("Bearer", pat);
        c.Timeout = TimeSpan.FromSeconds(30);
        return c;
    }

    public async Task<List<BranchInfo>> GetBranchesAsync(string repoUrl, string? pat, CancellationToken ct)
    {
        var parsed = ParseRepo(repoUrl) ?? throw new InvalidOperationException("Not a valid GitHub repository URL.");
        using var c = Client(pat);
        var result = new List<BranchInfo>();
        for (var page = 1; page <= 5; page++)
        {
            var url = $"https://api.github.com/repos/{parsed.owner}/{parsed.repo}/branches?per_page=100&page={page}";
            using var resp = await c.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub API {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var arr = doc.RootElement.EnumerateArray().ToList();
            result.AddRange(arr.Select(e => new BranchInfo(e.GetProperty("name").GetString() ?? "")));
            if (arr.Count < 100) break;
        }
        return result.Where(b => b.Name.Length > 0).ToList();
    }

    public async Task<CommitInfo?> GetLatestCommitAsync(string repoUrl, string branch, string? pat, CancellationToken ct)
    {
        var parsed = ParseRepo(repoUrl) ?? throw new InvalidOperationException("Not a valid GitHub repository URL.");
        using var c = Client(pat);
        var url = $"https://api.github.com/repos/{parsed.owner}/{parsed.repo}/commits/{Uri.EscapeDataString(branch)}";
        using var resp = await c.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var sha = root.GetProperty("sha").GetString() ?? "";
        var commit = root.GetProperty("commit");
        var message = commit.GetProperty("message").GetString() ?? "";
        var author = commit.GetProperty("author").GetProperty("name").GetString() ?? "";
        DateTime? date = commit.GetProperty("author").TryGetProperty("date", out var d) && d.TryGetDateTime(out var dt) ? dt : null;
        return new CommitInfo(sha, sha.Length >= 7 ? sha[..7] : sha, message, author, date);
    }

    public async Task<TestResult> TestConnectionAsync(string repoUrl, string? pat, CancellationToken ct)
    {
        var parsed = ParseRepo(repoUrl);
        if (parsed is null) return new TestResult(false, "Not a valid GitHub repository URL.");
        try
        {
            using var c = Client(pat);
            var url = $"https://api.github.com/repos/{parsed.Value.owner}/{parsed.Value.repo}";
            using var resp = await c.GetAsync(url, ct);
            if (resp.IsSuccessStatusCode)
                return new TestResult(true, $"Connected to {parsed.Value.owner}/{parsed.Value.repo}.");
            if ((int)resp.StatusCode == 404)
                return new TestResult(false, "Repository not found (private repo needs a valid PAT).");
            if ((int)resp.StatusCode == 401)
                return new TestResult(false, "Authentication failed — check the Personal Access Token.");
            return new TestResult(false, $"GitHub API returned {(int)resp.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new TestResult(false, ex.Message);
        }
    }
}
