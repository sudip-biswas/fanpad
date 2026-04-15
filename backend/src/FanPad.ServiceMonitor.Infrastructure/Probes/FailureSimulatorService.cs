using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace FanPad.ServiceMonitor.Infrastructure.Probes;

/// <summary>
/// Singleton failure injection service. Controls which mock scenario is active.
/// Probes check this before making real (or simulated) calls.
///
/// Scenarios supported:
///   MailgunCompleteOutage        — Mailgun 503, 0% success
///   MailgunPartialDegradation    — Mailgun degraded, 62% success, 4800ms latency
///   MailgunHighLatency           — Mailgun slow but 99% success
///   MailgunHighErrorRate         — Mailgun fast but 30% error rate
///   SesCompleteOutage            — SES 503, 0% success
///   SesPartialDegradation        — SES degraded
///   BothEmailProvidersDown       — Both Mailgun + SES down (no email fallback)
///   TwilioCompleteOutage         — Twilio 503
///   TwilioPartialDegradation     — Twilio degraded
///   TwilioHighLatency            — Twilio slow
///   MailgunRecovering            — Mailgun improving (92% success)
///   TwilioRecovering             — Twilio improving (90% success)
///   MailgunThenSesFailure        — After failover to SES, SES also fails
///   IntermittentMailgunErrors    — Random flapping errors
///   AllServicesDown              — Full catastrophic outage
/// </summary>
public class FailureSimulatorService : IFailureSimulator
{
    private readonly ILogger<FailureSimulatorService> _logger;
    private volatile FailureScenario _activeScenario = FailureScenario.None;
    private int _callCount = 0;

    public FailureSimulatorService(ILogger<FailureSimulatorService> logger)
    {
        _logger = logger;
    }

    public FailureScenario ActiveScenario => _activeScenario;
    public bool IsSimulationActive => _activeScenario != FailureScenario.None;

    public void ActivateScenario(FailureScenario scenario)
    {
        _activeScenario = scenario;
        _callCount = 0;
        _logger.LogWarning("SIMULATION ACTIVATED: {Scenario}", scenario);
    }

    public void ClearScenario()
    {
        var previous = _activeScenario;
        _activeScenario = FailureScenario.None;
        _callCount = 0;
        _logger.LogInformation("SIMULATION CLEARED (was: {Previous})", previous);
    }

    public SimulatedProbeState? GetSimulatedState(ServiceProvider provider, ProbeSource source)
    {
        if (_activeScenario == FailureScenario.None) return null;

        Interlocked.Increment(ref _callCount);
        var callN = _callCount;

        return _activeScenario switch
        {
            FailureScenario.MailgunCompleteOutage =>
                provider == ServiceProvider.Mailgun
                    ? Outage(provider, "SERVICE_UNAVAILABLE", "Mailgun API returned 503 Service Unavailable. All message queues halted.", _activeScenario)
                    : null,

            FailureScenario.MailgunPartialDegradation =>
                provider == ServiceProvider.Mailgun
                    ? Degraded(provider, latencyMs: 4800, successRate: 62m, errorRate: 38m,
                        "SMTP_TIMEOUT", "Connection timeouts on SMTP relay. Some messages queued, delivery delayed.", _activeScenario)
                    : null,

            FailureScenario.MailgunHighLatency =>
                provider == ServiceProvider.Mailgun
                    ? Degraded(provider, latencyMs: 7200, successRate: 99m, errorRate: 1m,
                        "HIGH_LATENCY", "Mailgun API responding slowly (p99: 7200ms). SLA breach imminent.", _activeScenario)
                    : null,

            FailureScenario.MailgunHighErrorRate =>
                provider == ServiceProvider.Mailgun
                    ? Degraded(provider, latencyMs: 220, successRate: 71m, errorRate: 29m,
                        "BOUNCE_RATE_ELEVATED", "Elevated bounce rate detected. Mailgun deliverability scoring dropped.", _activeScenario)
                    : null,

            FailureScenario.SesCompleteOutage =>
                provider == ServiceProvider.Ses
                    ? Outage(provider, "AWS_SES_UNAVAILABLE", "AWS SES endpoint unreachable. us-east-1 regional event in progress.", _activeScenario)
                    : null,

            FailureScenario.SesPartialDegradation =>
                provider == ServiceProvider.Ses
                    ? Degraded(provider, latencyMs: 3100, successRate: 74m, errorRate: 26m,
                        "SES_THROTTLE", "SES sending rate throttled. MessageRejected errors on high-volume sends.", _activeScenario)
                    : null,

            FailureScenario.BothEmailProvidersDown =>
                (provider == ServiceProvider.Mailgun || provider == ServiceProvider.Ses)
                    ? Outage(provider, "EMAIL_BLACKOUT",
                        $"{provider} is unreachable. Region-wide email delivery infrastructure impacted.", _activeScenario)
                    : null,

            FailureScenario.TwilioCompleteOutage =>
                provider == ServiceProvider.Twilio
                    ? Outage(provider, "TWILIO_UNAVAILABLE", "Twilio API unreachable. All SMS/voice routes offline.", _activeScenario)
                    : null,

            FailureScenario.TwilioPartialDegradation =>
                provider == ServiceProvider.Twilio
                    ? Degraded(provider, latencyMs: 3600, successRate: 68m, errorRate: 32m,
                        "CARRIER_ISSUES", "Carrier-level delivery failures. US carrier interconnect degraded.", _activeScenario)
                    : null,

            FailureScenario.TwilioHighLatency =>
                provider == ServiceProvider.Twilio
                    ? Degraded(provider, latencyMs: 5500, successRate: 98m, errorRate: 2m,
                        "PROCESSING_DELAY", "Twilio processing latency elevated. Messages delayed up to 45s.", _activeScenario)
                    : null,

            FailureScenario.MailgunRecovering =>
                provider == ServiceProvider.Mailgun
                    ? new SimulatedProbeState(HealthStatus.Degraded, 1200, 92m, 8m,
                        "RECOVERING", "Mailgun recovering from earlier outage. Success rate improving.", _activeScenario.ToString())
                    : null,

            FailureScenario.TwilioRecovering =>
                provider == ServiceProvider.Twilio
                    ? new SimulatedProbeState(HealthStatus.Degraded, 900, 90m, 10m,
                        "RECOVERING", "Twilio recovering. Most carrier routes restored.", _activeScenario.ToString())
                    : null,

            FailureScenario.MailgunThenSesFailure =>
                // Mailgun always down; SES goes down after 3 calls (simulates cascade)
                provider == ServiceProvider.Mailgun
                    ? Outage(provider, "SERVICE_UNAVAILABLE", "Mailgun still unreachable.", _activeScenario)
                    : provider == ServiceProvider.Ses && callN > 3
                        ? Outage(provider, "AWS_SES_UNAVAILABLE", "SES also failing — cascade event. No email fallback available.", _activeScenario)
                        : null,

            FailureScenario.IntermittentMailgunErrors =>
                // Every 3rd call is an error (flapping)
                provider == ServiceProvider.Mailgun && callN % 3 == 0
                    ? Degraded(provider, latencyMs: 800, successRate: 67m, errorRate: 33m,
                        "INTERMITTENT", "Intermittent errors. Pattern: 2 successes, 1 failure.", _activeScenario)
                    : null,

            FailureScenario.AllServicesDown =>
                provider switch
                {
                    ServiceProvider.Mailgun => Outage(provider, "FULL_OUTAGE", "Mailgun unreachable.", _activeScenario),
                    ServiceProvider.Ses     => Outage(provider, "FULL_OUTAGE", "AWS SES unreachable.", _activeScenario),
                    ServiceProvider.Twilio  => Outage(provider, "FULL_OUTAGE", "Twilio unreachable.", _activeScenario),
                    _                       => null
                },

            _ => null
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static SimulatedProbeState Outage(ServiceProvider p, string code, string message, FailureScenario s) =>
        new(HealthStatus.MajorOutage, null, 0m, 100m, code, message, s.ToString());

    private static SimulatedProbeState Degraded(ServiceProvider p, int latencyMs, decimal successRate,
        decimal errorRate, string code, string message, FailureScenario s) =>
        new(HealthStatus.Degraded, latencyMs, successRate, errorRate, code, message, s.ToString());
}
