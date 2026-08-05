using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace SqlAccess.Api.Services;

public interface IEmailService
{
    /// <summary>Sends the build-failure alert to the configured recipient. Never throws.</summary>
    Task<bool> SendBuildFailureAsync(string branch, IReadOnlyList<string> errors, CancellationToken ct);
}

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
        var host = s["Host"];
        var user = s["UserName"];
        var pass = s["Password"];
        var from = s["FromAddress"];
        var to = s["AlertRecipient"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(pass) ||
            string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            _log.LogWarning("SMTP not fully configured — skipping build-failure email.");
            return false;
        }

        var port = int.TryParse(s["Port"], out var p) ? p : 587;

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
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(s["FromDisplayName"] ?? "WorkProvider360", from));
            msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = $"❌ DACPAC build failed — branch '{branch}' ({errors.Count} error(s))";
            msg.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

            using var client = new SmtpClient { Timeout = 20_000 };
            // Brevo on 587 uses STARTTLS.
            await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, ct);
            await client.AuthenticateAsync(user, pass, ct);
            await client.SendAsync(msg, ct);
            await client.DisconnectAsync(true, ct);

            _log.LogInformation("Build-failure email sent to {To}", to);
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send build-failure email.");
            return false;
        }
    }
}
