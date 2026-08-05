using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Cicd.Services;

namespace SqlAccess.Api.Cicd.Controllers;

[ApiController]
[Authorize]
[Route("api/websites")]
public class WebsitesController : ControllerBase
{
    private readonly IWebsiteService _svc;
    public WebsitesController(IWebsiteService svc) => _svc = svc;

    [HttpGet]
    public async Task<ActionResult<List<WebsiteListItem>>> List(CancellationToken ct)
        => Ok(await _svc.ListAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WebsiteDetail>> Get(int id, CancellationToken ct)
        => await _svc.GetAsync(id, ct) is { } d ? Ok(d) : NotFound();

    [HttpPost]
    public async Task<ActionResult<WebsiteDetail>> Create([FromBody] UpsertWebsiteRequest req, CancellationToken ct)
    {
        var created = await _svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = created.WebsiteId }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<WebsiteDetail>> Update(int id, [FromBody] UpsertWebsiteRequest req, CancellationToken ct)
        => await _svc.UpdateAsync(id, req, ct) is { } d ? Ok(d) : NotFound();

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
        => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet("{id:int}/branches")]
    public async Task<ActionResult<List<BranchInfo>>> Branches(int id, CancellationToken ct)
    {
        try { return Ok(await _svc.GetBranchesAsync(id, ct)); }
        catch (Exception ex) { return StatusCode(502, new { message = ex.Message }); }
    }

    [HttpGet("{id:int}/commit")]
    public async Task<ActionResult<CommitInfo>> LatestCommit(int id, [FromQuery] string branch, CancellationToken ct)
    {
        var c = await _svc.GetLatestCommitAsync(id, branch, ct);
        return c is null ? NotFound() : Ok(c);
    }

    [HttpPost("test-git")]
    public async Task<ActionResult<TestResult>> TestGit([FromBody] TestGitRequest req, CancellationToken ct)
        => Ok(await _svc.TestGitAsync(req, ct));

    /// <summary>Load branches for an unsaved repo (used by the create wizard).</summary>
    [HttpPost("branches-preview")]
    public async Task<ActionResult<List<BranchInfo>>> BranchesPreview([FromBody] TestGitRequest req, CancellationToken ct)
    {
        try { return Ok(await _svc.PreviewBranchesAsync(req.RepositoryUrl, req.Pat, ct)); }
        catch (Exception ex) { return StatusCode(502, new { message = ex.Message }); }
    }

    [HttpPost("test-ftp")]
    public async Task<ActionResult<TestResult>> TestFtp([FromBody] TestFtpRequest req, CancellationToken ct)
        => Ok(await _svc.TestFtpAsync(req, ct));

    [HttpGet("build-templates")]
    public ActionResult<IReadOnlyList<BuildTemplate>> BuildTemplates() => Ok(_svc.BuildTemplates());
}
