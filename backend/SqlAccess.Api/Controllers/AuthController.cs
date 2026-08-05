using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Models;
using SqlAccess.Api.Services;

namespace SqlAccess.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ITokenService _tokens;
    private readonly ILogger<AuthController> _log;

    public AuthController(IConfiguration config, ITokenService tokens, ILogger<AuthController> log)
    {
        _config = config;
        _tokens = tokens;
        _log = log;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest req)
    {
        var expectedUser = _config["Auth:Username"] ?? string.Empty;
        var storedHash = _config["Auth:PasswordHash"] ?? string.Empty;

        var userOk = string.Equals(req.Username?.Trim(), expectedUser, StringComparison.OrdinalIgnoreCase);
        var passOk = !string.IsNullOrEmpty(storedHash) && PasswordHasher.Verify(req.Password ?? "", storedHash);

        if (!userOk || !passOk)
        {
            _log.LogWarning("Failed login attempt for user '{User}'", req.Username);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var (token, expiresAt) = _tokens.Create(expectedUser);
        return Ok(new LoginResponse(token, expiresAt, expectedUser));
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { username = User.Identity?.Name });
}
