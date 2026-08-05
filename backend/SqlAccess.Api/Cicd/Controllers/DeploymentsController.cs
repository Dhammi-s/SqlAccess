using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Cicd.Models;
using SqlAccess.Api.Cicd.Services;

namespace SqlAccess.Api.Cicd.Controllers;

[ApiController]
[Authorize]
[Route("api/deployments")]
public class DeploymentsController : ControllerBase
{
    private readonly ICicdDeploymentService _svc;
    public DeploymentsController(ICicdDeploymentService svc) => _svc = svc;

    private string CurrentUser => User.Identity?.Name ?? "unknown";

    /// <summary>Queue a deployment. Returns the new deployment id to subscribe to via SignalR.</summary>
    [HttpPost]
    public async Task<ActionResult<object>> Trigger([FromBody] TriggerDeployRequest req, CancellationToken ct)
    {
        try
        {
            var id = await _svc.TriggerAsync(req.WebsiteId, req.Branch, CurrentUser, ct);
            return Ok(new { deploymentId = id });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:int}/retry")]
    public async Task<ActionResult<object>> Retry(int id, CancellationToken ct)
    {
        var newId = await _svc.RetryAsync(id, CurrentUser, ct);
        return newId is null ? NotFound() : Ok(new { deploymentId = newId });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        => await _svc.CancelAsync(id, ct) ? NoContent() : NotFound();

    [HttpGet]
    public async Task<ActionResult<List<DeploymentListItem>>> List(
        [FromQuery] int? websiteId, [FromQuery] int take = 50, CancellationToken ct = default)
        => Ok(await _svc.ListAsync(websiteId, take, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DeploymentListItem>> Get(int id, CancellationToken ct)
        => await _svc.GetAsync(id, ct) is { } d ? Ok(d) : NotFound();

    [HttpGet("{id:int}/logs")]
    public async Task<ActionResult<List<LogEntry>>> Logs(int id, [FromQuery] long after = 0, CancellationToken ct = default)
        => Ok(await _svc.GetLogsAsync(id, after, ct));
}
