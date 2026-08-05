using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Models;
using SqlAccess.Api.Services;

namespace SqlAccess.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class DeployController : ControllerBase
{
    private readonly IDeploymentService _svc;
    private readonly ISourceBuildService _build;

    public DeployController(IDeploymentService svc, ISourceBuildService build)
    {
        _svc = svc;
        _build = build;
    }

    /// <summary>List branches of the configured GitHub repository.</summary>
    [HttpGet("branches")]
    public async Task<ActionResult<List<BranchInfo>>> Branches(CancellationToken ct)
    {
        try { return Ok(await _build.ListBranchesAsync(ct)); }
        catch (Exception ex) { return StatusCode(502, new { message = ex.Message }); }
    }

    /// <summary>Build a DACPAC from a branch (downloads source + compiles with DacFx).</summary>
    [HttpPost("build")]
    public async Task<ActionResult<BuildResult>> Build([FromBody] BuildFromBranchRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Branch))
            return BadRequest(new { message = "branch is required." });
        return Ok(await _build.BuildFromBranchAsync(req.Branch, ct));
    }

    /// <summary>Upload a .dacpac. Returns an id used by the run endpoint.</summary>
    [HttpPost("upload")]
    [RequestSizeLimit(200_000_000)] // 200 MB
    public async Task<ActionResult<DacpacInfo>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "No file uploaded." });
        try
        {
            await using var stream = file.OpenReadStream();
            var info = await _svc.SaveDacpacAsync(stream, file.FileName, ct);
            return Ok(info);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Generate a script for, or deploy, the DACPAC to a single agency database.</summary>
    [HttpPost("run")]
    public async Task<ActionResult<DeployResult>> Run([FromBody] DeployRunRequest req, CancellationToken ct)
        => Ok(await _svc.RunAsync(req, ct));
}
