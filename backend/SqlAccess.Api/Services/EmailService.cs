using System.Net;
using brevo_csharp.Api;
using brevo_csharp.Client;
using brevo_csharp.Model;

namespace SqlAccess.Api.Services;

public interface IEmailService
{
    /// <summary>Sends the build-failure alert to the configured recipient. Never throws.</summary>
    Task<bool> SendBuildFailureAsync(string branch, IReadOnlyList<string> errors, CancellationToken ct);
}

/// <summary>
/// Sends transactional email using the official Brevo SDK (brevo_csharp) over HTTPS.
/// Requires a Brevo API key (starts with "xkeysib-") in config "Smtp:ApiKey" —
/// this is NOT the SMTP key ("xsmtpsib-"); the API rejects SMTP keys with 401.
/// </summary>
public sealed class EmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _log;

    public EmailService(IConfiguration config, ILogger<EmailService> log)
    {
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

        try
        {
            var config = new brevo_csharp.Client.Configuration();
            config.ApiKey["api-key"] = apiKey;

            var api = new TransactionalEmailsApi(config);
            var email = new SendSmtpEmail(
                sender: new SendSmtpEmailSender(fromName, from),
                to: new List<SendSmtpEmailTo> { new SendSmtpEmailTo(to) },
                htmlContent: html,
                subject: $"❌ DACPAC build failed — branch '{branch}' ({errors.Count} error(s))");

            var result = await api.SendTransacEmailAsync(email);
            _log.LogInformation("Build-failure email sent to {To} via Brevo SDK (messageId {Id}).", to, result?.MessageId);
            return true;
        }
        catch (ApiException ex)
        {
            _log.LogError("Brevo SDK error {Code}: {Content}", ex.ErrorCode, (object?)ex.ErrorContent);
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send build-failure email via Brevo SDK.");
            return false;
        }
    }
}
