using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FanPad.ServiceMonitor.Infrastructure.Probes;

/// <summary>
/// Health probe for AWS SES.
/// Internal: sends a test message via SES SDK to a verified test recipient.
/// External: checks the AWS Health Dashboard API feed.
/// </summary>
public class SesProbeService : BaseProbeService
{
    public override ServiceProvider Provider => ServiceProvider.Ses;

    public SesProbeService(IFailureSimulator sim, ILogger<SesProbeService> logger, HttpClient httpClient)
        : base(sim, logger, httpClient) { }

    protected override async Task<ProbeResult> RunInternalProbeInternalAsync(ServiceConfig config, CancellationToken ct)
    {
        try
        {
            // In production: use AWSSDK.SimpleEmail to send a test email.
            // We simulate a probe response for the assignment.
            var (_, latencyMs) = await TimeAsync(async () =>
            {
                await Task.Delay(Random.Shared.Next(100, 400), ct);
                return true;
            });

            Logger.LogDebug("[SES] Internal probe completed in {Latency}ms", latencyMs);
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
            // AWS Health Dashboard RSS/JSON — simplified check against known SES status endpoint
            var (response, latencyMs) = await TimeAsync(async () =>
                await HttpClient.GetAsync(
                    "https://health.aws.amazon.com/health/status",
                    ct));

            // The real AWS health API requires authentication; for the demo we parse the public page.
            // We treat a 200 OK as healthy.
            var healthStatus = response.IsSuccessStatusCode
                ? HealthStatus.Operational
                : HealthStatus.Unknown;

            return healthStatus == HealthStatus.Operational
                ? ProbeResult.Operational(ProbeSource.ExternalStatusPage, latencyMs)
                : ProbeResult.Unknown(ProbeSource.ExternalStatusPage, $"AWS Health Dashboard returned HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return SafeFallback(ex, ProbeSource.ExternalStatusPage);
        }
    }
}
