using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SqlAccess.Api.Vault.Models;
using SqlAccess.Api.Vault.Services;

namespace SqlAccess.Api.Vault.Controllers;

[ApiController]
[Route("api/vault")]
public class VaultController : ControllerBase
{
    private readonly IVaultService _vault;
    private readonly IVaultAuditService _audit;

    public VaultController(IVaultService vault, IVaultAuditService audit)
    {
        _vault = vault;
        _audit = audit;
    }

    private string? CurrentUser => User.Identity?.Name;

    // ============ Application authentication (machine-to-machine) ============

    /// <summary>Exchange ClientId + ClientSecret for a scoped vault token.</summary>
    [AllowAnonymous]
    [EnableRateLimiting("vault-auth")]
    [HttpPost("login")]
    public async Task<ActionResult<VaultTokenResponse>> Login([FromBody] VaultLoginRequest req, CancellationToken ct)
    {
        var result = await _vault.AuthenticateAsync(req, ct);
        return result is null ? Unauthorized(new { message = "Invalid client credentials." }) : Ok(result);
    }

    /// <summary>Application reads a secret it is authorized for (requires an application token).</summary>
    [Authorize(Policy = "VaultApp")]
    [EnableRateLimiting("vault-read")]
    [HttpGet("secrets/{name}")]
    public async Task<ActionResult<SecretValueResponse>> GetSecret(string name, CancellationToken ct)
    {
        var appId = int.Parse(User.FindFirst("vault_app_id")!.Value);
        var appName = User.Identity?.Name ?? "";
        var result = await _vault.GetSecretForApplicationAsync(appId, appName, name, ct);
        return result is null ? NotFound(new { message = "Secret not found or not authorized." }) : Ok(result);
    }

    // ============ Applications (admin) ============

    [Authorize(Policy = "VaultAdmin")]
    [HttpPost("register-application")]
    public async Task<ActionResult<RegisterAppResponse>> RegisterApplication([FromBody] RegisterAppRequest req, CancellationToken ct)
        => Ok(await _vault.RegisterApplicationAsync(req, ct));

    [Authorize(Policy = "VaultAdmin")]
    [HttpGet("applications")]
    public async Task<ActionResult<List<AppListItem>>> Applications(CancellationToken ct)
        => Ok(await _vault.ListApplicationsAsync(ct));

    // ============ Secrets (admin) ============

    [Authorize(Policy = "VaultAdmin")]
    [HttpPost("secrets")]
    public async Task<ActionResult<SecretListItem>> CreateSecret([FromBody] CreateSecretRequest req, CancellationToken ct)
    {
        try { return Ok(await _vault.CreateSecretAsync(req, CurrentUser, ct)); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }

    [Authorize(Policy = "VaultAdmin")]
    [HttpGet("secrets")]
    public async Task<ActionResult<List<SecretListItem>>> ListSecrets([FromQuery] string? search, CancellationToken ct)
        => Ok(await _vault.ListSecretsAsync(search, ct));

    [Authorize(Policy = "VaultAdmin")]
    [HttpPut("secrets/{id:int}")]
    public async Task<ActionResult<SecretListItem>> UpdateSecret(int id, [FromBody] UpdateSecretRequest req, CancellationToken ct)
        => await _vault.UpdateSecretAsync(id, req, CurrentUser, ct) is { } s ? Ok(s) : NotFound();

    [Authorize(Policy = "VaultAdmin")]
    [HttpDelete("secrets/{id:int}")]
    public async Task<IActionResult> DeleteSecret(int id, CancellationToken ct)
        => await _vault.DeleteSecretAsync(id, ct) ? NoContent() : NotFound();

    [Authorize(Policy = "VaultAdmin")]
    [HttpPost("rotate-secret")]
    public async Task<ActionResult<SecretListItem>> RotateSecret([FromBody] RotateSecretRequest req, CancellationToken ct)
        => await _vault.RotateSecretAsync(req, CurrentUser, ct) is { } s ? Ok(s) : NotFound();

    [Authorize(Policy = "VaultAdmin")]
    [HttpGet("versions/{secretId:int}")]
    public async Task<ActionResult<List<SecretVersionItem>>> Versions(int secretId, CancellationToken ct)
        => Ok(await _vault.GetVersionsAsync(secretId, ct));

    [Authorize(Policy = "VaultAdmin")]
    [HttpPost("restore-version")]
    public async Task<ActionResult<SecretListItem>> RestoreVersion([FromBody] RestoreVersionRequest req, CancellationToken ct)
        => await _vault.RestoreVersionAsync(req, CurrentUser, ct) is { } s ? Ok(s) : NotFound();

    // ============ Access assignment (admin) ============

    [Authorize(Policy = "VaultAdmin")]
    [HttpPost("assign-secret")]
    public async Task<ActionResult<ApplicationSecretItem>> AssignSecret([FromBody] AssignSecretRequest req, CancellationToken ct)
        => await _vault.AssignSecretAsync(req, ct) is { } a ? Ok(a) : NotFound(new { message = "Application or secret not found." });

    [Authorize(Policy = "VaultAdmin")]
    [HttpGet("assignments")]
    public async Task<ActionResult<List<ApplicationSecretItem>>> Assignments(CancellationToken ct)
        => Ok(await _vault.ListAssignmentsAsync(ct));

    [Authorize(Policy = "VaultAdmin")]
    [HttpDelete("assignments/{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
        => await _vault.RevokeAsync(id, ct) ? NoContent() : NotFound();

    // ============ Audit (admin) ============

    [Authorize(Policy = "VaultAdmin")]
    [HttpGet("auditlogs")]
    public async Task<ActionResult<List<AuditLogItem>>> AuditLogs([FromQuery] int take = 200, CancellationToken ct = default)
        => Ok(await _audit.ListAsync(take, ct));
}
