using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class RoutingState
{
    public Guid Id { get; set; }
    public ServiceType ServiceType { get; set; }
    public Guid ActiveServiceConfigId { get; set; }
    public RoutingAction Action { get; set; } = RoutingAction.Auto;
    public string? Reason { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "system";

    // Navigation
    public ServiceConfig? ActiveServiceConfig { get; set; }
}

public class FailoverEvent
{
    public Guid Id { get; set; }
    public ServiceType ServiceType { get; set; }
    public ServiceProvider FromProvider { get; set; }
    public ServiceProvider ToProvider { get; set; }
    public Guid? IncidentId { get; set; }
    public FailoverAuthority Authority { get; set; } = FailoverAuthority.HumanApproved;
    public string? AgentRecommendation { get; set; }
    public string? WorkPlan { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? RevertedAt { get; set; }
    public bool Success { get; set; } = true;
    public bool IsSimulated { get; set; }

    // Navigation
    public Incident? Incident { get; set; }
}

public class FailoverApproval
{
    public Guid Id { get; set; }
    public Guid AgentDecisionId { get; set; }
    public ServiceType ServiceType { get; set; }
    public ServiceProvider FromProvider { get; set; }
    public ServiceProvider ToProvider { get; set; }
    public string AgentRecommendation { get; set; } = string.Empty;
    public string WorkPlan { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";     // pending, approved, rejected
    public string? ReviewedBy { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Navigation
    public AgentDecision? AgentDecision { get; set; }
}
