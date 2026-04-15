using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FanPad.ServiceMonitor.Api.Controllers;

[ApiController]
[Route("api/routing")]
public class RoutingController : ControllerBase
{
    private readonly IRoutingService _routing;
    private readonly AppDbContext _db;
    private readonly IHubContext<ServiceStatusHub> _hub;

    public RoutingController(IRoutingService routing, AppDbContext db, IHubContext<ServiceStatusHub> hub)
    {
        _routing = routing;
        _db = db;
        _hub = hub;
    }

    /// <summary>GET /api/routing — Current routing state for all service types.</summary>
    [HttpGet]
    public async Task<IActionResult> GetRoutes(CancellationToken ct)
    {
        var routes = await _routing.GetAllRoutesAsync(ct);
        return Ok(routes.Select(r => new
        {
            service_type = r.ServiceType.ToString(),
            active_provider = r.ActiveServiceConfig?.Provider.ToString(),
            display_name = r.ActiveServiceConfig?.DisplayName,
            is_primary = r.ActiveServiceConfig?.IsPrimary,
            action = r.Action.ToString(),
            reason = r.Reason,
            changed_at = r.ChangedAt,
            changed_by = r.ChangedBy
        }));
    }

    /// <summary>GET /api/routing/failover-events — History of all failover events.</summary>
    [HttpGet("failover-events")]
    public async Task<IActionResult> GetFailoverEvents(CancellationToken ct)
    {
        var events = await _db.FailoverEvents
            .OrderByDescending(e => e.InitiatedAt)
            .Take(50)
            .Select(e => new
            {
                id = e.Id,
                service_type = e.ServiceType.ToString(),
                from_provider = e.FromProvider.ToString(),
                to_provider = e.ToProvider.ToString(),
                authority = e.Authority.ToString(),
                work_plan = e.WorkPlan,
                approved_by = e.ApprovedBy,
                initiated_at = e.InitiatedAt,
                completed_at = e.CompletedAt,
                reverted_at = e.RevertedAt,
                success = e.Success,
                is_simulated = e.IsSimulated
            })
            .ToListAsync(ct);

        return Ok(events);
    }

    /// <summary>GET /api/routing/approvals — Pending failover approval requests.</summary>
    [HttpGet("approvals")]
    public async Task<IActionResult> GetPendingApprovals(CancellationToken ct)
    {
        var approvals = await _db.FailoverApprovals
            .Include(a => a.AgentDecision)
            .Where(a => a.Status == "pending" && a.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(a => a.RequestedAt)
            .Select(a => new
            {
                id = a.Id,
                service_type = a.ServiceType.ToString(),
                from_provider = a.FromProvider.ToString(),
                to_provider = a.ToProvider.ToString(),
                agent_recommendation = a.AgentRecommendation,
                work_plan = a.WorkPlan,
                status = a.Status,
                requested_at = a.RequestedAt,
                expires_at = a.ExpiresAt,
                agent_decision_id = a.AgentDecisionId
            })
            .ToListAsync(ct);

        return Ok(approvals);
    }

    /// <summary>POST /api/routing/approvals/{id}/approve — Human approves a failover recommendation.</summary>
    [HttpPost("approvals/{id:guid}/approve")]
    public async Task<IActionResult> ApproveFailover(Guid id, [FromBody] ApprovalRequest req, CancellationToken ct)
    {
        var approval = await _db.FailoverApprovals.FindAsync(new object[] { id }, ct);
        if (approval == null) return NotFound();
        if (approval.Status != "pending") return BadRequest(new { error = "Approval is no longer pending" });
        if (approval.ExpiresAt < DateTime.UtcNow) return BadRequest(new { error = "Approval has expired" });

        // Execute the failover
        var failoverEvent = await _routing.ExecuteFailoverAsync(
            approval.ServiceType,
            approval.ToProvider,
            FailoverAuthority.HumanApproved,
            approval.AgentRecommendation,
            approval.WorkPlan,
            isSimulated: false,
            ct: ct);

        // Mark approval as approved
        approval.Status = "approved";
        approval.ReviewedBy = req.ReviewedBy ?? "operator";
        approval.ReviewNote = req.Note;
        approval.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Notify dashboard
        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.FailoverExecuted, new
            {
                failover_event_id = failoverEvent.Id,
                service_type = approval.ServiceType.ToString(),
                from_provider = approval.FromProvider.ToString(),
                to_provider = approval.ToProvider.ToString(),
                approved_by = approval.ReviewedBy,
                work_plan = approval.WorkPlan
            }, ct);

        return Ok(new
        {
            approval_id = id,
            failover_event_id = failoverEvent.Id,
            status = "approved",
            message = $"Failover executed: {approval.FromProvider} → {approval.ToProvider} for {approval.ServiceType}"
        });
    }

    /// <summary>POST /api/routing/approvals/{id}/reject — Human rejects a failover recommendation.</summary>
    [HttpPost("approvals/{id:guid}/reject")]
    public async Task<IActionResult> RejectFailover(Guid id, [FromBody] ApprovalRequest req, CancellationToken ct)
    {
        var approval = await _db.FailoverApprovals.FindAsync(new object[] { id }, ct);
        if (approval == null) return NotFound();

        approval.Status = "rejected";
        approval.ReviewedBy = req.ReviewedBy ?? "operator";
        approval.ReviewNote = req.Note;
        approval.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { approval_id = id, status = "rejected" });
    }

    /// <summary>POST /api/routing/revert/{serviceType} — Manually revert to primary provider.</summary>
    [HttpPost("revert/{serviceType}")]
    public async Task<IActionResult> RevertToPrimary(string serviceType, [FromBody] RevertRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<ServiceType>(serviceType, true, out var st))
            return BadRequest(new { error = "Invalid service type" });

        var route = await _routing.RevertToPrimaryAsync(st, req.Reason ?? "Manual revert by operator", ct);

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.FailoverExecuted, new
            {
                service_type = st.ToString(),
                active_provider = route.ActiveServiceConfig?.Provider.ToString(),
                reason = route.Reason
            }, ct);

        return Ok(new
        {
            service_type = st.ToString(),
            active_provider = route.ActiveServiceConfig?.Provider.ToString(),
            message = "Reverted to primary provider"
        });
    }
}

public record ApprovalRequest(string? ReviewedBy, string? Note);
public record RevertRequest(string? Reason);
