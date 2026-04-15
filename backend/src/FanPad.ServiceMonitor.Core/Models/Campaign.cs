using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class Campaign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ArtistName { get; set; }
    public ServiceType[] ServiceTypes { get; set; } = Array.Empty<ServiceType>();
    public DateTime? ScheduledAt { get; set; }
    public CampaignGateStatus GateStatus { get; set; } = CampaignGateStatus.Go;
    public DateTime? GateCheckedAt { get; set; }
    public string? HoldReason { get; set; }
    public JsonDocument? RerouteDetail { get; set; }
    public Guid? AgentDecisionId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public AgentDecision? AgentDecision { get; set; }
}
