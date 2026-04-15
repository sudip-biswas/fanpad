using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FanPad.ServiceMonitor.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly IAgentOrchestrationService _agent;
    private readonly IEnumerable<IHealthProbeService> _probes;
    private readonly AppDbContext _db;
    private readonly IHubContext<ServiceStatusHub> _hub;
    private readonly ILogger<HealthController> _logger;

    public HealthController(
        IAgentOrchestrationService agent,
        IEnumerable<IHealthProbeService> probes,
        AppDbContext db,
        IHubContext<ServiceStatusHub> hub,
        ILogger<HealthController> logger)
    {
        _agent = agent;
        _probes = probes;
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>GET /api/health/summary — Current health status for all services (dashboard view).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var configs = await _db.ServiceConfigs
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var latestResults = await _db.HealthCheckResults
            .Where(h => h.CheckedAt >= cutoff)
            .GroupBy(h => h.ServiceConfigId)
            .Select(g => g.OrderByDescending(x => x.CheckedAt).First())
            .ToListAsync(ct);

        var routes = await _db.RoutingStates
            .Include(r => r.ActiveServiceConfig)
            .ToListAsync(ct);

        var openIncidents = await _db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved)
            .Include(i => i.ServiceConfig)
            .ToListAsync(ct);

        var summary = configs.Select(config =>
        {
            var result = latestResults.FirstOrDefault(r => r.ServiceConfigId == config.Id);
            var route = routes.FirstOrDefault(r => r.ActiveServiceConfig?.Id == config.Id);
            var incidents = openIncidents.Where(i => i.ServiceConfigId == config.Id).ToList();

            return new
            {
                id = config.Id,
                provider = config.Provider.ToString(),
                service_type = config.ServiceType.ToString(),
                display_name = config.DisplayName,
                is_primary = config.IsPrimary,
                is_active_route = route != null,
                status = result?.Status.ToString() ?? "unknown",
                latency_ms = result?.LatencyMs,
                success_rate = result?.SuccessRate,
                error_rate = result?.ErrorRate,
                last_checked = result?.CheckedAt,
                is_simulated = result?.IsSimulated ?? false,
                simulation_scenario = result?.SimulationScenario,
                open_incidents = incidents.Select(i => new
                {
                    id = i.Id,
                    title = i.Title,
                    severity = i.Severity.ToString(),
                    opened_at = i.OpenedAt
                })
            };
        });

        return Ok(summary);
    }

    /// <summary>POST /api/health/evaluate — Manually trigger agent evaluation.</summary>
    [HttpPost("evaluate")]
    public async Task<IActionResult> TriggerEvaluation([FromQuery] string? provider, CancellationToken ct)
    {
        ServiceProvider? filterProvider = null;
        if (!string.IsNullOrEmpty(provider) && Enum.TryParse<ServiceProvider>(provider, true, out var p))
            filterProvider = p;

        var decision = await _agent.RunManualEvaluationAsync(filterProvider, ct);

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.AgentDecisionLogged, new
            {
                id = decision.Id,
                decision = decision.Decision,
                work_plan = decision.WorkPlan,
                decided_at = decision.DecidedAt
            }, ct);

        return Ok(new
        {
            decision_id = decision.Id,
            decision = decision.Decision,
            work_plan = decision.WorkPlan,
            reasoning = decision.Reasoning,
            duration_ms = decision.DurationMs
        });
    }

    /// <summary>GET /api/health/history — Recent health check results for charts.</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(
        [FromQuery] string? provider,
        [FromQuery] int minutes = 60,
        CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.AddMinutes(-Math.Min(minutes, 1440));
        var query = _db.HealthCheckResults
            .Include(h => h.ServiceConfig)
            .Where(h => h.CheckedAt >= since)
            .AsQueryable();

        if (!string.IsNullOrEmpty(provider) && Enum.TryParse<ServiceProvider>(provider, true, out var pEnum))
            query = query.Where(h => h.ServiceConfig!.Provider == pEnum);

        var results = await query
            .OrderByDescending(h => h.CheckedAt)
            .Take(200)
            .Select(h => new
            {
                id = h.Id,
                provider = h.ServiceConfig!.Provider.ToString(),
                service_type = h.ServiceConfig.ServiceType.ToString(),
                checked_at = h.CheckedAt,
                status = h.Status.ToString(),
                latency_ms = h.LatencyMs,
                success_rate = h.SuccessRate,
                error_rate = h.ErrorRate,
                error_code = h.ErrorCode,
                is_simulated = h.IsSimulated,
                simulation_scenario = h.SimulationScenario
            })
            .ToListAsync(ct);

        return Ok(results);
    }

    /// <summary>GET /api/health/decisions — Agent decision log.</summary>
    [HttpGet("decisions")]
    public async Task<IActionResult> GetDecisions([FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var decisions = await _db.AgentDecisions
            .OrderByDescending(d => d.DecidedAt)
            .Take(limit)
            .Select(d => new
            {
                id = d.Id,
                trigger_type = d.TriggerType,
                decision = d.Decision,
                decision_detail = d.DecisionDetail,
                work_plan = d.WorkPlan,
                reasoning = d.Reasoning,
                decided_at = d.DecidedAt,
                duration_ms = d.DurationMs,
                model_used = d.ModelUsed
            })
            .ToListAsync(ct);

        return Ok(decisions);
    }
}
