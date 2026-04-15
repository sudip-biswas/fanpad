using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Models;

namespace FanPad.ServiceMonitor.Core.Interfaces;

/// <summary>
/// Orchestrates the Claude AI agent to evaluate service health,
/// make failover recommendations, and generate work plans.
/// </summary>
public interface IAgentOrchestrationService
{
    /// <summary>
    /// Scheduled health evaluation — runs on a timer, checks all services,
    /// persists decisions, and sends real-time updates.
    /// </summary>
    Task<AgentDecision> RunScheduledHealthCheckAsync(CancellationToken ct = default);

    /// <summary>
    /// Campaign gate check — determines if a specific campaign is safe to launch.
    /// Returns GO / HOLD / REROUTE with a work plan.
    /// </summary>
    Task<CampaignGateResult> EvaluateCampaignGateAsync(Guid campaignId, CancellationToken ct = default);

    /// <summary>
    /// Manual trigger — human-initiated evaluation for a specific service or scenario.
    /// </summary>
    Task<AgentDecision> RunManualEvaluationAsync(ServiceProvider? filterProvider = null, CancellationToken ct = default);
}

public record CampaignGateResult(
    Guid CampaignId,
    CampaignGateStatus GateStatus,
    string WorkPlan,
    string? RerouteToProvider,
    Guid AgentDecisionId,
    IReadOnlyList<ChannelGateResult> ChannelResults
);

public record ChannelGateResult(
    ServiceType ServiceType,
    ServiceProvider OriginalProvider,
    ServiceProvider? ReroutedToProvider,
    CampaignGateStatus Status,
    string Reason
);
