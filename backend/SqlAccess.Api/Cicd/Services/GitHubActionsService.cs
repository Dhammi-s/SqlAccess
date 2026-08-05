using System.Net.Http.Json;
using System.Text.Json;

namespace SqlAccess.Api.Cicd.Services;

public record WorkflowRun(long Id, string Status, string? Conclusion, string HtmlUrl, int RunNumber, string? HeadSha);
public record WorkflowStep(string Job, string Name, string Status, string? Conclusion);

public interface IGitHubActionsService
{
    /// <summary>Triggers a workflow_dispatch. Throws with a clear message on failure (e.g. missing 'workflow' scope).</summary>
    Task DispatchAsync(string repoUrl, string workflowFile, string branch, string? pat, CancellationToken ct);

    /// <summary>Finds the run created by our dispatch (polls, since dispatch returns no id).</summary>
    Task<long?> FindRunAsync(string repoUrl, string workflowFile, string branch, string? pat, DateTime notBeforeUtc, CancellationToken ct);

    Task<WorkflowRun?> GetRunAsync(string repoUrl, long runId, string? pat, CancellationToken ct);
    Task<List<WorkflowStep>> GetStepsAsync(string repoUrl, long runId, string? pat, CancellationToken ct);
}

public sealed class GitHubActionsService : IGitHubActionsService
{
    private readonly IHttpClientFactory _http;
    public GitHubActionsService(IHttpClientFactory http) => _http = http;

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

    private static (string owner, string repo) Repo(string repoUrl) =>
        GitHubService.ParseRepo(repoUrl) ?? throw new InvalidOperationException("Not a valid GitHub repository URL.");

    public async Task DispatchAsync(string repoUrl, string workflowFile, string branch, string? pat, CancellationToken ct)
    {
        var (owner, repo) = Repo(repoUrl);
        using var c = Client(pat);
        var url = $"https://api.github.com/repos/{owner}/{repo}/actions/workflows/{Uri.EscapeDataString(workflowFile)}/dispatches";
        using var resp = await c.PostAsJsonAsync(url, new { @ref = branch }, ct);
        if (resp.IsSuccessStatusCode) return;

        var body = await resp.Content.ReadAsStringAsync(ct);
        var hint = (int)resp.StatusCode switch
        {
            403 => " (the Personal Access Token needs the 'workflow' scope / actions:write)",
            404 => $" (workflow '{workflowFile}' not found, or the token can't see it)",
            422 => $" (branch '{branch}' not found, or the workflow has no 'workflow_dispatch' trigger)",
            _ => "",
        };
        throw new InvalidOperationException($"GitHub dispatch failed {(int)resp.StatusCode}{hint}: {body}");
    }

    public async Task<long?> FindRunAsync(
        string repoUrl, string workflowFile, string branch, string? pat, DateTime notBeforeUtc, CancellationToken ct)
    {
        var (owner, repo) = Repo(repoUrl);
        using var c = Client(pat);
        var url = $"https://api.github.com/repos/{owner}/{repo}/actions/runs" +
                  $"?event=workflow_dispatch&branch={Uri.EscapeDataString(branch)}&per_page=15";
        using var resp = await c.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        long? best = null;
        foreach (var run in doc.RootElement.GetProperty("workflow_runs").EnumerateArray())
        {
            var path = run.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            if (!path.EndsWith("/" + workflowFile, StringComparison.OrdinalIgnoreCase)) continue;
            if (run.TryGetProperty("created_at", out var ca) && ca.TryGetDateTime(out var created)
                && created.ToUniversalTime() < notBeforeUtc.AddSeconds(-30)) continue;
            var id = run.GetProperty("id").GetInt64();
            if (best is null || id > best) best = id;
        }
        return best;
    }

    public async Task<WorkflowRun?> GetRunAsync(string repoUrl, long runId, string? pat, CancellationToken ct)
    {
        var (owner, repo) = Repo(repoUrl);
        using var c = Client(pat);
        using var resp = await c.GetAsync($"https://api.github.com/repos/{owner}/{repo}/actions/runs/{runId}", ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var r = doc.RootElement;
        return new WorkflowRun(
            r.GetProperty("id").GetInt64(),
            r.GetProperty("status").GetString() ?? "",
            r.TryGetProperty("conclusion", out var cc) ? cc.GetString() : null,
            r.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
            r.TryGetProperty("run_number", out var n) ? n.GetInt32() : 0,
            r.TryGetProperty("head_sha", out var s) ? s.GetString() : null);
    }

    public async Task<List<WorkflowStep>> GetStepsAsync(string repoUrl, long runId, string? pat, CancellationToken ct)
    {
        var (owner, repo) = Repo(repoUrl);
        using var c = Client(pat);
        using var resp = await c.GetAsync($"https://api.github.com/repos/{owner}/{repo}/actions/runs/{runId}/jobs", ct);
        var steps = new List<WorkflowStep>();
        if (!resp.IsSuccessStatusCode) return steps;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var jobName = job.GetProperty("name").GetString() ?? "job";
            if (!job.TryGetProperty("steps", out var stepsEl)) continue;
            foreach (var st in stepsEl.EnumerateArray())
            {
                steps.Add(new WorkflowStep(
                    jobName,
                    st.GetProperty("name").GetString() ?? "",
                    st.GetProperty("status").GetString() ?? "",
                    st.TryGetProperty("conclusion", out var cc) ? cc.GetString() : null));
            }
        }
        return steps;
    }
}
