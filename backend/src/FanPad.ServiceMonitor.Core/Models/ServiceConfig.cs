using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Models;

public class ServiceConfig
{
    public Guid Id { get; set; }
    public ServiceProvider Provider { get; set; }
    public ServiceType ServiceType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public int Priority { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public JsonDocument ConfigJson { get; set; } = JsonDocument.Parse("{}");
    public string? StatusPageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation
    public ICollection<HealthCheckResult> HealthCheckResults { get; set; } = new List<HealthCheckResult>();
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}
