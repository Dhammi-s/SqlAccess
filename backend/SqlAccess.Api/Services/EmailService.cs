using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SqlAccess.Api.Services;

public interface IEmailService
{
    /// <summary>Sends the build-failure alert to the configured recipient. Never throws.</summary>
    Task<bool> SendBuildFailureAsync(string branch, IReadOnlyList<string> errors, CancellationToken ct);
}

/// <summary>
/// Sends transactional email through Brevo's HTTP API (https://api.brevo.com/v3/smtp/email).
/// Uses HTTPS/443 — works where outbound SMTP ports are blocked (most hosts, and CI sandboxes).
/// Requires a Brevo API key (starts with "xkeysib-") in config "Smtp:ApiKey".
/// </summary>
public sealed class EmailService : IEmailService
{
    private const string ApiUrl = "https://api.brevo.com/v3/smtp/email";

    private readonly IHttpClientFactory _http;
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _log;

    public EmailService(IHttpClientFactory http, IConfiguration config, ILogger<EmailService> log)
    {
        _http = http;
        _config = config;
        _log = log;
    }

    public async Task<bool> SendBuildFailureAsync(string branch, IReadOnlyList<string> errors, CancellationToken ct)
    {
        var s = _config.GetSection("Smtp");
        var apiKey = s["ApiKey"];
        var from = s["FromAddress"];
        var fromName = s["FromDisplayName"] ?? "WorkProvider360";
        var to = s["AlertRecipient"];

        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            _log.LogWarning("Brevo not fully configured (need Smtp:ApiKey, FromAddress, AlertRecipient) — skipping email.");
            return false;
        }

        var rows = errors.Count == 0
            ? "<li>(no detailed messages)</li>"
            : string.Join("", errors.Select(e => $"<li style='margin-bottom:6px'>{WebUtility.HtmlEncode(e)}</li>"));

        var html = $@"
<div style='font-family:Segoe UI,Arial,sans-serif;color:#0f172a'>
  <h2 style='color:#dc2626;margin:0 0 8px'>❌ DACPAC build failed</h2>
  <p style='margin:0 0 4px'><b>Repository:</b> {WebUtility.HtmlEncode(_config["GitHub:Repo"])}</p>
  <p style='margin:0 0 12px'><b>Branch:</b> {WebUtility.HtmlEncode(branch)}</p>
  <p style='margin:0 0 6px'><b>{errors.Count} error(s):</b></p>
  <ul style='background:#fef2f2;border:1px solid #fecaca;border-radius:8px;padding:12px 12px 12px 28px'>
    {rows}
  </ul>
  <p style='color:#64748b;font-size:12px'>Sent by SQL Access — WorkProvider360 deployment.</p>
</div>";

        var payload = new
        {
            sender = new { name = fromName, email = from },
            to = new[] { new { email = to } },
            subject = $"❌ DACPAC build failed — branch '{branch}' ({errors.Count} error(s))",
            htmlContent = html,
        };

        try
        {
            using var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Add("api-key", apiKey);
            req.Headers.Add("accept", "application/json");

            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
            {
                _log.LogInformation("Build-failure email sent to {To} via Brevo API.", to);
                return true;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            _log.LogError("Brevo API {Status}: {Body}", (int)resp.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send build-failure email via Brevo API.");
            return false;
        }
    }
}
