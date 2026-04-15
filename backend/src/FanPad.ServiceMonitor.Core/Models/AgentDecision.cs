using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class AgentDecision
{
    public Guid Id { get; set; }
    public string TriggerType { get; set; } = string.Empty;    // "scheduled", "campaign_gate", "manual"
    public JsonDocument TriggerContext { get; set; } = JsonDocument.Parse("{}");
    public string InputSummary { get; set; } = string.Empty;
    public string? Reasoning { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? DecisionDetail { get; set; }
    public JsonDocument ActionsTaken { get; set; } = JsonDocument.Parse("[]");
    public string? WorkPlan { get; set; }
    public string ModelUsed { get; set; } = "claude-sonnet-4-6";
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public DateTime DecidedAt { get; set; }
    public int? DurationMs { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid? FailoverEventId { get; set; }

    // Navigation
    public Incident? Incident { get; set; }
    public FailoverEvent? FailoverEvent { get; set; }
    public ICollection<Campaign> Campaigns { get; set; } = new List<Campaign>();
}
