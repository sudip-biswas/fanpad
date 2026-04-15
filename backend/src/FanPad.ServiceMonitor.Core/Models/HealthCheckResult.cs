using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class HealthCheckResult
{
    public Guid Id { get; set; }
    public Guid ServiceConfigId { get; set; }
    public DateTime CheckedAt { get; set; }
    public HealthStatus Status { get; set; }
    public int? LatencyMs { get; set; }
    public decimal? SuccessRate { get; set; }
    public decimal? ErrorRate { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public HealthStatus? ExternalStatus { get; set; }
    public HealthStatus? InternalStatus { get; set; }
    public JsonDocument ProbeDetailJson { get; set; } = JsonDocument.Parse("{}");
    public bool IsSimulated { get; set; }
    public string? SimulationScenario { get; set; }

    // Navigation
    public ServiceConfig? ServiceConfig { get; set; }
}
