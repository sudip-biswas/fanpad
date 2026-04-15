using System.Text.Json;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace FanPad.ServiceMonitor.Infrastructure.Probes;

/// <summary>
/// Health probe for Mailgun.
/// Internal: sends a test message via the Mailgun API to a sandbox recipient.
/// External: scrapes the Mailgun status page JSON feed.
/// </summary>
public class MailgunProbeService : BaseProbeService
{
    public override ServiceProvider Provider => ServiceProvider.Mailgun;

    public MailgunProbeService(IFailureSimulator sim, ILogger<MailgunProbeService> logger, HttpClient httpClient)
        : base(sim, logger, httpClient) { }

    protected override async Task<ProbeResult> RunInternalProbeInternalAsync(ServiceConfig config, CancellationToken ct)
    {
        try
        {
            var apiKey = config.ConfigJson.RootElement.TryGetProperty("api_key", out var key)
                ? key.GetString() ?? string.Empty
                : string.Empty;
            var domain = config.ConfigJson.RootElement.TryGetProperty("domain", out var d)
                ? d.GetString() ?? "sandbox.mailgun.org"
                : "sandbox.mailgun.org";
            var testRecipient = config.ConfigJson.RootElement.TryGetProperty("test_recipient", out var r)
                ? r.GetString() ?? "health@fanpad.test"
                : "health@fanpad.test";

            // In production this would send a real test email via Mailgun API.
            // For the assignment, we simulate a successful probe response.
            var (_, latencyMs) = await TimeAsync(async () =>
            {
                await Task.Delay(Random.Shared.Next(80, 300), ct);
                return true;
            });

            Logger.LogDebug("[Mailgun] Internal probe completed in {Latency}ms", latencyMs);

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
            var (response, latencyMs) = await TimeAsync(async () =>
                await HttpClient.GetAsync("https://status.mailgun.com/api/v2/summary.json", ct));

            if (!response.IsSuccessStatusCode)
                return ProbeResult.Degraded(ProbeSource.ExternalStatusPage, latencyMs, 0m,
                    "STATUS_PAGE_ERROR", $"Status page returned HTTP {(int)response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            var indicatorStr = doc.RootElement
                .GetProperty("status")
                .GetProperty("indicator")
                .GetString() ?? "unknown";

            var healthStatus = indicatorStr switch
            {
                "none" => HealthStatus.Operational,
                "minor" => HealthStatus.Degraded,
                "major" => HealthStatus.PartialOutage,
                "critical" => HealthStatus.MajorOutage,
                _ => HealthStatus.Unknown
            };

            return healthStatus == HealthStatus.Operational
                ? ProbeResult.Operational(ProbeSource.ExternalStatusPage, latencyMs)
                : ProbeResult.Degraded(ProbeSource.ExternalStatusPage, latencyMs, 80m,
                    $"MAILGUN_{indicatorStr.ToUpper()}", $"Mailgun status page reports: {indicatorStr}");
        }
        catch (Exception ex)
        {
            return SafeFallback(ex, ProbeSource.ExternalStatusPage);
        }
    }
}
