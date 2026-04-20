using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FanPad.ServiceMonitor.Api.Controllers;

/// <summary>
/// Demo/testing controller for injecting failure scenarios.
/// This is the primary mechanism for demo walkthrough and comprehensive testing.
/// All endpoints are tagged [SimulationOnly] for clarity.
/// </summary>
[ApiController]
[Route("api/simulation")]
public class SimulationController : ControllerBase
{
    private readonly IFailureSimulator _simulator;
    private readonly IAgentOrchestrationService _agent;
    private readonly IHubContext<ServiceStatusHub> _hub;
    private readonly ILogger<SimulationController> _logger;

    public SimulationController(
        IFailureSimulator simulator,
        IAgentOrchestrationService agent,
        IHubContext<ServiceStatusHub> hub,
        ILogger<SimulationController> logger)
    {
        _simulator = simulator;
        _agent = agent;
        _hub = hub;
        _logger = logger;
    }

    /// <summary>GET /api/simulation/scenarios — List all available failure scenarios.</summary>
    [HttpGet("scenarios")]
    public IActionResult GetScenarios()
    {
        var scenarios = Enum.GetValues<FailureScenario>()
            .Select(s => new
            {
                id = s.ToString(),
                name = FormatScenarioName(s),
                description = GetScenarioDescription(s),
                affectedServices = GetAffectedServices(s)
            });

        return Ok(new
        {
            activeScenario = _simulator.ActiveScenario.ToString(),
            isSimulationActive = _simulator.IsSimulationActive,
            scenarios
        });
    }

    /// <summary>POST /api/simulation/activate — Activate a failure scenario.</summary>
    [HttpPost("activate")]
    public async Task<IActionResult> ActivateScenario([FromBody] ActivateScenarioRequest req, CancellationToken ct)
    {
        if (!Enum.TryParse<FailureScenario>(req.Scenario, true, out var scenario))
            return BadRequest(new { error = $"Unknown scenario: {req.Scenario}" });

        _simulator.ActivateScenario(scenario);

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.SimulationActivated, new
            {
                scenario = scenario.ToString(),
                name = FormatScenarioName(scenario),
                description = GetScenarioDescription(scenario)
            }, ct);

        _logger.LogWarning("[DEMO] Simulation activated: {Scenario}", scenario);

        // Optionally trigger immediate agent evaluation
        if (req.TriggerAgentEvaluation)
        {
            var decision = await _agent.RunManualEvaluationAsync(ct: ct);
            await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
                .SendAsync(HubEvents.AgentDecisionLogged, new
                {
                    id = decision.Id,
                    decision = decision.Decision,
                    workPlan = decision.WorkPlan,
                    decidedAt = decision.DecidedAt
                }, ct);

            return Ok(new
            {
                scenario = scenario.ToString(),
                activated = true,
                agentDecision = new
                {
                    id = decision.Id,
                    decision = decision.Decision,
                    workPlan = decision.WorkPlan
                }
            });
        }

        return Ok(new { scenario = scenario.ToString(), activated = true });
    }

    /// <summary>POST /api/simulation/clear — Clear the active failure scenario.</summary>
    [HttpPost("clear")]
    public async Task<IActionResult> ClearScenario(CancellationToken ct)
    {
        var previous = _simulator.ActiveScenario;
        _simulator.ClearScenario();

        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.SimulationCleared, new { previousScenario = previous.ToString() }, ct);

        return Ok(new { cleared = true, previousScenario = previous.ToString() });
    }

    /// <summary>GET /api/simulation/status — Current simulation status.</summary>
    [HttpGet("status")]
    public IActionResult GetStatus() =>
        Ok(new
        {
            isActive = _simulator.IsSimulationActive,
            activeScenario = _simulator.ActiveScenario.ToString(),
            description = GetScenarioDescription(_simulator.ActiveScenario)
        });

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatScenarioName(FailureScenario s) =>
        string.Concat(s.ToString().Select((c, i) => i > 0 && char.IsUpper(c) ? $" {c}" : c.ToString()));

    private static string GetScenarioDescription(FailureScenario s) => s switch
    {
        FailureScenario.None                    => "All services nominal. No simulation active.",
        FailureScenario.MailgunCompleteOutage   => "Mailgun returns 503. Zero deliverability. Agent should recommend failover to SES immediately.",
        FailureScenario.MailgunPartialDegradation => "Mailgun at 62% success, 4800ms latency. Agent should open incident and recommend failover.",
        FailureScenario.MailgunHighLatency      => "Mailgun latency at 7200ms but 99% success. Agent should monitor, not failover.",
        FailureScenario.MailgunHighErrorRate    => "Mailgun error rate 29%, low latency. Agent should open incident and recommend failover.",
        FailureScenario.SesCompleteOutage       => "AWS SES unreachable. Tests fallback behavior when the backup is also down.",
        FailureScenario.SesPartialDegradation   => "SES throttled at 74% success. Tests secondary provider degradation.",
        FailureScenario.BothEmailProvidersDown  => "Both Mailgun and SES down. Agent should hold all email campaigns and escalate.",
        FailureScenario.TwilioCompleteOutage    => "Twilio unreachable. Agent should hold SMS campaigns.",
        FailureScenario.TwilioPartialDegradation => "Twilio at 68% success due to carrier issues. Agent should open incident.",
        FailureScenario.TwilioHighLatency       => "Twilio at 5500ms latency. Agent monitors for SLA breach.",
        FailureScenario.MailgunRecovering       => "Mailgun returning to health (92% success). Agent should detect recovery trend and recommend revert.",
        FailureScenario.TwilioRecovering        => "Twilio recovering (90% success). Agent monitors for full recovery.",
        FailureScenario.MailgunThenSesFailure   => "Mailgun fails, failover to SES initiated, then SES also fails. Tests cascade handling.",
        FailureScenario.IntermittentMailgunErrors => "Mailgun flapping (every 3rd probe fails). Tests agent threshold vs transient error logic.",
        FailureScenario.AllServicesDown         => "All providers offline. Full campaign hold + critical escalation.",
        _ => "Unknown scenario"
    };

    private static string[] GetAffectedServices(FailureScenario s) => s switch
    {
        FailureScenario.MailgunCompleteOutage    => new[] { "mailgun" },
        FailureScenario.MailgunPartialDegradation => new[] { "mailgun" },
        FailureScenario.MailgunHighLatency       => new[] { "mailgun" },
        FailureScenario.MailgunHighErrorRate     => new[] { "mailgun" },
        FailureScenario.SesCompleteOutage        => new[] { "ses" },
        FailureScenario.SesPartialDegradation    => new[] { "ses" },
        FailureScenario.BothEmailProvidersDown   => new[] { "mailgun", "ses" },
        FailureScenario.TwilioCompleteOutage     => new[] { "twilio" },
        FailureScenario.TwilioPartialDegradation => new[] { "twilio" },
        FailureScenario.TwilioHighLatency        => new[] { "twilio" },
        FailureScenario.MailgunRecovering        => new[] { "mailgun" },
        FailureScenario.TwilioRecovering         => new[] { "twilio" },
        FailureScenario.MailgunThenSesFailure    => new[] { "mailgun", "ses" },
        FailureScenario.IntermittentMailgunErrors => new[] { "mailgun" },
        FailureScenario.AllServicesDown          => new[] { "mailgun", "ses", "twilio" },
        _ => Array.Empty<string>()
    };
}

public record ActivateScenarioRequest(string Scenario, bool TriggerAgentEvaluation = true);
