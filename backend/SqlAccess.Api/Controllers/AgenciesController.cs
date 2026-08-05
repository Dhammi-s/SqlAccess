using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlAccess.Api.Models;
using SqlAccess.Api.Services;

namespace SqlAccess.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AgenciesController : ControllerBase
{
    private readonly IAgencyService _svc;

    public AgenciesController(IAgencyService svc) => _svc = svc;

    /// <summary>List agencies. Secrets are masked.</summary>
    [HttpGet]
    public async Task<ActionResult<List<AgencyListItem>>> List(
        [FromQuery] bool includeArchived = false, CancellationToken ct = default)
        => Ok(await _svc.ListAsync(includeArchived, ct));

    /// <summary>Get one agency with decrypted secrets (for editing).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AgencyDetail>> Get(int id, CancellationToken ct)
    {
        var a = await _svc.GetAsync(id, ct);
        return a is null ? NotFound() : Ok(a);
    }

    [HttpPost]
    public async Task<ActionResult<AgencyDetail>> Create(
        [FromBody] CreateAgencyRequest req, CancellationToken ct)
    {
        var created = await _svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = created.AgencyId }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AgencyDetail>> Update(
        int id, [FromBody] UpdateAgencyRequest req, CancellationToken ct)
    {
        var updated = await _svc.UpdateAsync(id, req, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    /// <summary>Soft-delete (archive) an agency. Use ?archived=false to restore.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Archive(int id, [FromQuery] bool archived = true, CancellationToken ct = default)
    {
        var ok = await _svc.ArchiveAsync(id, archived, ct);
        return ok ? NoContent() : NotFound();
    }

    /// <summary>Test the stored connection for an agency.</summary>
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<TestConnectionResult>> Test(int id, CancellationToken ct)
        => Ok(await _svc.TestAsync(id, ct));

    /// <summary>Test an ad-hoc connection string before saving.</summary>
    [HttpPost("test")]
    public async Task<ActionResult<TestConnectionResult>> TestAdHoc(
        [FromBody] TestConnectionRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ConnectionString))
            return BadRequest(new { message = "connectionString is required." });
        return Ok(await _svc.TestAdHocAsync(req.ConnectionString, ct));
    }

    /// <summary>List the SQL Server database roles on this agency's database.</summary>
    [HttpGet("{id:int}/roles")]
    public async Task<ActionResult<DbRolesResult>> GetRoles(int id, CancellationToken ct)
        => Ok(await _svc.GetRolesAsync(id, ct));

    /// <summary>Create a new database role on this agency's database (optionally read-only).</summary>
    [HttpPost("{id:int}/roles")]
    public async Task<ActionResult<CreateRoleResult>> CreateRole(
        int id, [FromBody] CreateRoleRequest req, CancellationToken ct)
        => Ok(await _svc.CreateRoleAsync(id, req, ct));
}

public record TestConnectionRequest(string ConnectionString);
