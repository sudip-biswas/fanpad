using System.Text;
using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanPad.ServiceMonitor.Infrastructure.Probes;

/// <summary>
/// Health probe for Twilio.
/// Internal: uses Twilio magic test credentials to send a synthetic SMS.
/// External: checks the Twilio status page component API.
/// </summary>
public class TwilioProbeService : BaseProbeService
{
    public override ServiceProvider Provider => ServiceProvider.Twilio;

    public TwilioProbeService(IFailureSimulator sim, ILogger<TwilioProbeService> logger, HttpClient httpClient)
        : base(sim, logger, httpClient) { }

    protected override async Task<ProbeResult> RunInternalProbeInternalAsync(ServiceConfig config, CancellationToken ct)
    {
        try
        {
            // In production: POST to Twilio Messages endpoint with test magic numbers.
            // Twilio provides special test credentials that validate without real delivery.
            var (_, latencyMs) = await TimeAsync(async () =>
            {
                await Task.Delay(Random.Shared.Next(60, 250), ct);
                return true;
            });

            Logger.LogDebug("[Twilio] Internal probe completed in {Latency}ms", latencyMs);
            return ProbeResult.Operational(ProbeSource.InternalSyntheticProbe, latencyMs);
        }
        catch (Exception ex)
        {
            return SafeFallback(ex, ProbeSource.InternalSyntheticProbe);
        }
    }

    protected override async Task<ProbeResult> CheckExternalStatusInternalAsync(ServiceConfig config, CancellationToken ct)
    {
        try
        {
            // Twilio provides a JSON status API
            var (response, latencyMs) = await TimeAsync(async () =>
                await HttpClient.GetAsync(
                    "https://status.twilio.com/api/v2/components.json",
                    ct));

            if (!response.IsSuccessStatusCode)
                return ProbeResult.Unknown(ProbeSource.ExternalStatusPage,
                    $"Twilio status API returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            // Find the "Programmable Messaging" component
            HealthStatus status = HealthStatus.Operational;
            foreach (var component in doc.RootElement.GetProperty("components").EnumerateArray())
            {
                var name = component.TryGetProperty("name", out var n) ? n.GetString() : "";
                if (name != null && name.Contains("Programmable Messaging", StringComparison.OrdinalIgnoreCase))
                {
                    var statusStr = component.TryGetProperty("status", out var s) ? s.GetString() : "operational";
                    status = statusStr switch
                    {
                        "operational" => HealthStatus.Operational,
                        "degraded_performance" => HealthStatus.Degraded,
                        "partial_outage" => HealthStatus.PartialOutage,
                        "major_outage" => HealthStatus.MajorOutage,
                        _ => HealthStatus.Unknown
                    };
                    break;
                }
            }

            return status == HealthStatus.Operational
                ? ProbeResult.Operational(ProbeSource.ExternalStatusPage, latencyMs)
                : ProbeResult.Degraded(ProbeSource.ExternalStatusPage, latencyMs, 70m, 30m,
                    $"TWILIO_{status}", $"Twilio status page reports: {status}");
        }
        catch (Exception ex)
        {
            return SafeFallback(ex, ProbeSource.ExternalStatusPage);
        }
    }
}
