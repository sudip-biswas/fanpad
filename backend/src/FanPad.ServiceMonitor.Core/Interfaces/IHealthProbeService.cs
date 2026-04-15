using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Models;

namespace FanPad.ServiceMonitor.Core.Interfaces;

/// <summary>
/// Contract for a service-specific health probe.
/// Each external service (Mailgun, SES, Twilio) implements this.
/// </summary>
public interface IHealthProbeService
{
    ServiceProvider Provider { get; }

    /// <summary>Runs a synthetic send probe against a test account.</summary>
    Task<ProbeResult> RunInternalProbeAsync(ServiceConfig config, CancellationToken ct = default);

    /// <summary>Fetches the official external status page for the provider.</summary>
    Task<ProbeResult> CheckExternalStatusAsync(ServiceConfig config, CancellationToken ct = default);
}

public record ProbeResult(
    ProbeSource Source,
    HealthStatus Status,
    int? LatencyMs,
    decimal? SuccessRate,
    decimal? ErrorRate,
    string? ErrorCode,
    string? ErrorMessage,
    Dictionary<string, object>? Detail = null
)
{
    public static ProbeResult Operational(ProbeSource source, int latencyMs, decimal successRate = 100m) =>
        new(source, HealthStatus.Operational, latencyMs, successRate, 100m - successRate, null, null);

    public static ProbeResult Degraded(ProbeSource source, int latencyMs, decimal successRate, string errorCode, string errorMessage) =>
        new(source, HealthStatus.Degraded, latencyMs, successRate, 100m - successRate, errorCode, errorMessage);

    public static ProbeResult Outage(ProbeSource source, string errorCode, string errorMessage) =>
        new(source, HealthStatus.MajorOutage, null, 0, 100, errorCode, errorMessage);

    public static ProbeResult Unknown(ProbeSource source, string errorMessage) =>
        new(source, HealthStatus.Unknown, null, null, null, "UNKNOWN", errorMessage);
}
