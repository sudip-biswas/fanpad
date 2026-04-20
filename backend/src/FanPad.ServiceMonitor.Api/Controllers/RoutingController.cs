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
            serviceType = r.ServiceType.ToString(),
            activeProvider = r.ActiveServiceConfig?.Provider.ToString(),
            displayName = r.ActiveServiceConfig?.DisplayName,
            isPrimary = r.ActiveServiceConfig?.IsPrimary,
            action = r.Action.ToString(),
            reason = r.Reason,
            changedAt = r.ChangedAt,
            changedBy = r.ChangedBy
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
                serviceType = e.ServiceType.ToString(),
                fromProvider = e.FromProvider.ToString(),
                toProvider = e.ToProvider.ToString(),
                authority = e.Authority.ToString(),
                workPlan = e.WorkPlan,
                approvedBy = e.ApprovedBy,
                initiatedAt = e.InitiatedAt,
                completedAt = e.CompletedAt,
                revertedAt = e.RevertedAt,
                success = e.Success,
                isSimulated = e.IsSimulated
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
                serviceType = a.ServiceType.ToString(),
                fromProvider = a.FromProvider.ToString(),
                toProvider = a.ToProvider.ToString(),
                agentRecommendation = a.AgentRecommendation,
                workPlan = a.WorkPlan,
                status = a.Status,
                requestedAt = a.RequestedAt,
                expiresAt = a.ExpiresAt,
                agentDecisionId = a.AgentDecisionId
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
                failoverEventId = failoverEvent.Id,
                serviceType = approval.ServiceType.ToString(),
                fromProvider = approval.FromProvider.ToString(),
                toProvider = approval.ToProvider.ToString(),
                approvedBy = approval.ReviewedBy,
                workPlan = approval.WorkPlan
            }, ct);

        return Ok(new
        {
            approvalId = id,
            failoverEventId = failoverEvent.Id,
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

        return Ok(new { approvalId = id, status = "rejected" });
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
                serviceType = st.ToString(),
                activeProvider = route.ActiveServiceConfig?.Provider.ToString(),
                reason = route.Reason
            }, ct);

        return Ok(new
        {
            serviceType = st.ToString(),
            activeProvider = route.ActiveServiceConfig?.Provider.ToString(),
            message = "Reverted to primary provider"
        });
    }
}

public record ApprovalRequest(string? ReviewedBy, string? Note);
public record RevertRequest(string? Reason);
