using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FanPad.ServiceMonitor.Api.Controllers;

[ApiController]
[Route("api/incidents")]
public class IncidentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ServiceStatusHub> _hub;

    public IncidentController(AppDbContext db, IHubContext<ServiceStatusHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>GET /api/incidents — List incidents (optionally filter by status).</summary>
    [HttpGet]
    public async Task<IActionResult> GetIncidents([FromQuery] string? status, CancellationToken ct)
    {
        var query = _db.Incidents.Include(i => i.ServiceConfig).Include(i => i.Updates).AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<IncidentStatus>(status, true, out var s))
            query = query.Where(i => i.Status == s);

        var incidents = await query
            .OrderByDescending(i => i.OpenedAt)
            .Take(50)
            .Select(i => new
            {
                id = i.Id,
                provider = i.ServiceConfig!.Provider.ToString(),
                display_name = i.ServiceConfig.DisplayName,
                title = i.Title,
                description = i.Description,
                severity = i.Severity.ToString(),
                status = i.Status.ToString(),
                opened_at = i.OpenedAt,
                resolved_at = i.ResolvedAt,
                work_plan = i.WorkPlan,
                is_simulated = i.IsSimulated,
                simulation_scenario = i.SimulationScenario,
                updates = i.Updates.OrderByDescending(u => u.CreatedAt).Select(u => new
                {
                    message = u.Message,
                    author = u.Author,
                    created_at = u.CreatedAt
                })
            })
            .ToListAsync(ct);

        return Ok(incidents);
    }

    /// <summary>GET /api/incidents/{id} — Get incident details.</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetIncident(Guid id, CancellationToken ct)
    {
        var incident = await _db.Incidents
            .Include(i => i.ServiceConfig)
            .Include(i => i.Updates)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (incident == null) return NotFound();
        return Ok(incident);
    }
}
