using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FanPad.ServiceMonitor.Api.Controllers;

[ApiController]
[Route("api/campaigns")]
public class CampaignGateController : ControllerBase
{
    private readonly IAgentOrchestrationService _agent;
    private readonly AppDbContext _db;
    private readonly IHubContext<ServiceStatusHub> _hub;

    public CampaignGateController(IAgentOrchestrationService agent, AppDbContext db, IHubContext<ServiceStatusHub> hub)
    {
        _agent = agent;
        _db = db;
        _hub = hub;
    }

    /// <summary>GET /api/campaigns — List all campaigns with gate status.</summary>
    [HttpGet]
    public async Task<IActionResult> GetCampaigns(CancellationToken ct)
    {
        var campaigns = await _db.Campaigns
            .OrderByDescending(c => c.ScheduledAt)
            .Select(c => new
            {
                id = c.Id,
                name = c.Name,
                artistName = c.ArtistName,
                serviceTypes = c.ServiceTypes.Select(s => s.ToString()),
                scheduledAt = c.ScheduledAt,
                gateStatus = c.GateStatus.ToString(),
                gateCheckedAt = c.GateCheckedAt,
                holdReason = c.HoldReason,
                createdAt = c.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(campaigns);
    }

    /// <summary>POST /api/campaigns/{id}/gate-check — Run agent gate check for a campaign.</summary>
    [HttpPost("{id:guid}/gate-check")]
    public async Task<IActionResult> CheckGate(Guid id, CancellationToken ct)
    {
        var campaign = await _db.Campaigns.FindAsync(new object[] { id }, ct);
        if (campaign == null) return NotFound();

        var result = await _agent.EvaluateCampaignGateAsync(id, ct);

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.CampaignGateChanged, new
            {
                campaignId = id,
                campaignName = campaign.Name,
                gateStatus = result.GateStatus.ToString(),
                workPlan = result.WorkPlan,
                channelResults = result.ChannelResults.Select(r => new
                {
                    serviceType = r.ServiceType.ToString(),
                    originalProvider = r.OriginalProvider.ToString(),
                    reroutedTo = r.ReroutedToProvider?.ToString(),
                    status = r.Status.ToString(),
                    reason = r.Reason
                })
            }, ct);

        return Ok(new
        {
            campaignId = id,
            gateStatus = result.GateStatus.ToString(),
            workPlan = result.WorkPlan,
            agentDecisionId = result.AgentDecisionId,
            channelResults = result.ChannelResults
        });
    }

    /// <summary>POST /api/campaigns/{id}/release — Manually release a held campaign.</summary>
    [HttpPost("{id:guid}/release")]
    public async Task<IActionResult> Release(Guid id, [FromBody] ReleaseRequest req, CancellationToken ct)
    {
        var campaign = await _db.Campaigns.FindAsync(new object[] { id }, ct);
        if (campaign == null) return NotFound();

        campaign.GateStatus = CampaignGateStatus.Go;
        campaign.HoldReason = null;
        campaign.GateCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.CampaignGateChanged, new
            {
                campaignId = id,
                campaignName = campaign.Name,
                gateStatus = "go",
                workPlan = req.Note ?? "Manually released by operator"
            }, ct);

        return Ok(new { campaignId = id, gateStatus = "go" });
    }
}

public record ReleaseRequest(string? Note);
