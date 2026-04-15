using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class Incident
{
    public Guid Id { get; set; }
    public Guid ServiceConfigId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IncidentSeverity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;
    public DateTime OpenedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? WorkPlan { get; set; }
    public Guid[]? AffectedCampaigns { get; set; }
    public bool IsSimulated { get; set; }
    public string? SimulationScenario { get; set; }

    // Navigation
    public ServiceConfig? ServiceConfig { get; set; }
    public ICollection<IncidentUpdate> Updates { get; set; } = new List<IncidentUpdate>();
    public ICollection<AgentDecision> AgentDecisions { get; set; } = new List<AgentDecision>();
    public ICollection<FailoverEvent> FailoverEvents { get; set; } = new List<FailoverEvent>();
}

public class IncidentUpdate
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Author { get; set; } = "agent";
    public DateTime CreatedAt { get; set; }

    public Incident? Incident { get; set; }
}
