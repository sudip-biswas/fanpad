using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FanPad.ServiceMonitor.Infrastructure.Agent;

/// <summary>
/// Claude-powered agent that evaluates service health, generates work plans,
/// and recommends or executes failover actions.
///
/// The agent uses tool_use (function calling) to:
///  1. Fetch external status page readings
///  2. Run internal synthetic probes
///  3. Query recent health history
///  4. Open or update incidents
///  5. Submit failover recommendations (requires human approval)
///  6. Hold or reroute campaigns
/// </summary>
public class AgentOrchestrationService : IAgentOrchestrationService
{
    private readonly AnthropicClient _anthropic;
    private readonly IEnumerable<IHealthProbeService> _probes;
    private readonly IRoutingService _routing;
    private readonly AppDbContext _db;
    private readonly ILogger<AgentOrchestrationService> _logger;

    private const string SystemPrompt = """
        You are the Fanpad Service Health Agent. Your job is to monitor the health of messaging
        services (Twilio, Mailgun, AWS SES) and ensure campaigns are only executed through healthy services.

        You have access to tools to:
        - Check official external status pages for each service
        - Run internal health probes (synthetic test sends using test accounts)
        - Read recent health check history from the database
        - Query open incidents
        - Create or update incidents
        - Submit failover recommendations for human review
        - Hold or reroute pending campaigns

        EVALUATION GUIDELINES:
        Use all available signals — external status pages, internal probe results, and recent history trends.

        When evaluating health, consider:
        1. External status page indicators (operational / degraded / partial_outage / major_outage)
        2. Internal probe results: delivery latency, success rate, error codes
        3. Trend over the last 15 minutes (improving vs. worsening trajectory)
        4. Campaign sensitivity (high-value or time-sensitive launches warrant more caution)

        FAILOVER DECISION THRESHOLDS:
        - Error rate < 5%, no external incident → Monitor only. Do NOT failover.
        - Error rate 5–15% OR external incident reported → Open incident, monitor closely, prepare recommendation.
        - Error rate > 15% sustained OR error rate > 5% + external incident confirmed → Recommend failover.
        - Complete outage (0% success or unreachable) → Immediately recommend failover. Hold all affected campaigns.
        - Primary AND fallback both down → Hold all affected campaigns, escalate critical alert, do NOT failover.

        MULTI-CHANNEL CAMPAIGNS:
        - If only one channel (email OR sms) fails, hold that channel only. Allow the healthy channel to proceed.
        - Only hold an entire campaign if ALL its channels are affected.

        FAILOVER AUTHORITY:
        - You NEVER execute failovers autonomously. Always submit a recommendation with a work plan.
        - The recommendation goes to a human operator for approval.
        - Exception: if a campaign is already in progress and an outage is detected mid-send, recommend immediate hold.

        WORK PLAN FORMAT:
        Always produce a concise work plan in plain English readable by a non-technical campaign manager.
        Include: what happened, what you recommend, what the operator needs to do, and estimated impact.

        REVERT TO PRIMARY:
        - After 3 consecutive clean probes on the original primary, recommend revert.
        - Clearly state the improvement trend in your recommendation.
        """;

    public AgentOrchestrationService(
        AnthropicClient anthropic,
        IEnumerable<IHealthProbeService> probes,
        IRoutingService routing,
        AppDbContext db,
        ILogger<AgentOrchestrationService> logger)
    {
        _anthropic = anthropic;
        _probes = probes;
        _routing = routing;
        _db = db;
        _logger = logger;
    }

    // ─── Public Entry Points ──────────────────────────────────────────────────

    public async Task<AgentDecision> RunScheduledHealthCheckAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Agent: scheduled health check starting");
        var context = new Dictionary<string, object> { ["trigger"] = "scheduled", ["at"] = DateTime.UtcNow };
        return await RunAgentAsync("scheduled", context, ct: ct);
    }

    public async Task<CampaignGateResult> EvaluateCampaignGateAsync(Guid campaignId, CancellationToken ct = default)
    {
        _logger.LogInformation("Agent: campaign gate evaluation for {CampaignId}", campaignId);

        var campaign = await _db.Campaigns.FindAsync(new object[] { campaignId }, ct)
            ?? throw new ArgumentException($"Campaign {campaignId} not found");

        var context = new Dictionary<string, object>
        {
            ["trigger"] = "campaign_gate",
            ["campaign_id"] = campaignId.ToString(),
            ["campaign_name"] = campaign.Name,
            ["artist"] = campaign.ArtistName ?? "unknown",
            ["channels"] = campaign.ServiceTypes.Select(s => s.ToString()).ToArray(),
            ["scheduled_at"] = campaign.ScheduledAt?.ToString("O") ?? "now"
        };

        var decision = await RunAgentAsync("campaign_gate", context, campaignId: campaignId, ct: ct);

        // Parse per-channel gate results from agent's actions_taken
        var channelResults = ParseChannelResults(decision, campaign);

        // Update campaign gate status in DB
        campaign.GateCheckedAt = DateTime.UtcNow;
        campaign.AgentDecisionId = decision.Id;

        var overallStatus = channelResults.All(r => r.Status == CampaignGateStatus.Go)
            ? CampaignGateStatus.Go
            : channelResults.Any(r => r.Status == CampaignGateStatus.Rerouted)
                ? CampaignGateStatus.Rerouted
                : CampaignGateStatus.Hold;

        campaign.GateStatus = overallStatus;
        campaign.HoldReason = overallStatus != CampaignGateStatus.Go ? decision.WorkPlan : null;
        await _db.SaveChangesAsync(ct);

        return new CampaignGateResult(
            campaignId,
            overallStatus,
            decision.WorkPlan ?? "No work plan generated.",
            null,
            decision.Id,
            channelResults
        );
    }

    public async Task<AgentDecision> RunManualEvaluationAsync(ServiceProvider? filterProvider = null, CancellationToken ct = default)
    {
        var context = new Dictionary<string, object>
        {
            ["trigger"] = "manual",
            ["filter_provider"] = filterProvider?.ToString() ?? "all",
            ["at"] = DateTime.UtcNow
        };
        return await RunAgentAsync("manual", context, ct: ct);
    }

    // ─── Core Agent Loop ──────────────────────────────────────────────────────

    private async Task<AgentDecision> RunAgentAsync(
        string triggerType,
        Dictionary<string, object> triggerContext,
        Guid? campaignId = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var decisionId = Guid.NewGuid();
        var actionsTaken = new List<object>();

        // Gather current state summary to inject as the first user message
        var inputSummary = await BuildInputSummaryAsync(ct);

        var messages = new List<Message>
        {
            new()
            {
                Role = RoleType.User,
                Content = new List<ContentBase>
                {
                    new TextContent
                    {
                        Text = campaignId.HasValue
                            ? $"Please evaluate the campaign gate for campaign ID {campaignId}. Context:\n{inputSummary}"
                            : $"Please perform a full service health evaluation. Context:\n{inputSummary}"
                    }
                }
            }
        };

        var tools = AgentTools.GetAllTools();
        string? finalDecision = null;
        string? finalReasoning = null;
        string? workPlan = null;
        int? promptTokens = null;
        int? completionTokens = null;

        // Agentic loop — continue until the model stops using tools
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var response = await _anthropic.Messages.GetClaudeMessageAsync(
                new MessageParameters
                {
                    Model = AnthropicModels.Claude35Sonnet,
                    MaxTokens = 4096,
                    System = new List<SystemMessage> { new() { Text = SystemPrompt } },
                    Messages = messages,
                    Tools = tools
                }, ct);

            promptTokens = (promptTokens ?? 0) + response.Usage.InputTokens;
            completionTokens = (completionTokens ?? 0) + response.Usage.OutputTokens;

            // Collect text reasoning
            foreach (var block in response.Content)
            {
                if (block is TextContent txt && !string.IsNullOrWhiteSpace(txt.Text))
                    finalReasoning = (finalReasoning ?? "") + txt.Text + "\n";
            }

            if (response.StopReason == StopReason.EndTurn)
            {
                // Agent is done - extract final decision and work plan from reasoning
                workPlan = ExtractWorkPlan(finalReasoning);
                finalDecision = ExtractDecision(finalReasoning);
                break;
            }

            if (response.StopReason == StopReason.ToolUse)
            {
                // Process tool calls and add results back
                var toolResultContent = new List<ContentBase>();

                foreach (var block in response.Content.OfType<ToolUseContent>())
                {
                    var toolResult = await ExecuteToolAsync(block, actionsTaken, ct);
                    toolResultContent.Add(new ToolResultContent
                    {
                        ToolUseId = block.Id,
                        Content = toolResult
                    });
                }

                // Add assistant turn with tool use
                messages.Add(new Message
                {
                    Role = RoleType.Assistant,
                    Content = response.Content
                });

                // Add user turn with tool results
                messages.Add(new Message
                {
                    Role = RoleType.User,
                    Content = toolResultContent
                });
            }
        }

        // Persist the decision
        var decision = new AgentDecision
        {
            Id = decisionId,
            TriggerType = triggerType,
            TriggerContext = JsonDocument.Parse(JsonSerializer.Serialize(triggerContext)),
            InputSummary = inputSummary,
            Reasoning = finalReasoning,
            Decision = finalDecision ?? "no_action",
            DecisionDetail = workPlan,
            ActionsTaken = JsonDocument.Parse(JsonSerializer.Serialize(actionsTaken)),
            WorkPlan = workPlan,
            ModelUsed = AnthropicModels.Claude35Sonnet,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            DecidedAt = DateTime.UtcNow,
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        _db.AgentDecisions.Add(decision);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agent decision complete: {Decision} in {DurationMs}ms | tokens: {Tokens}",
            decision.Decision, decision.DurationMs, (promptTokens ?? 0) + (completionTokens ?? 0));

        return decision;
    }

    // ─── Tool Execution Dispatcher ────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(ToolUseContent toolUse, List<object> actionsTaken, CancellationToken ct)
    {
        _logger.LogDebug("Agent tool call: {ToolName} | input: {Input}", toolUse.Name, toolUse.Input);

        try
        {
            var result = toolUse.Name switch
            {
                AgentTools.CheckExternalStatus  => await Tool_CheckExternalStatus(toolUse.Input, ct),
                AgentTools.RunInternalProbe     => await Tool_RunInternalProbe(toolUse.Input, ct),
                AgentTools.GetHealthHistory     => await Tool_GetHealthHistory(toolUse.Input, ct),
                AgentTools.GetOpenIncidents     => await Tool_GetOpenIncidents(ct),
                AgentTools.GetRoutingState      => await Tool_GetRoutingState(ct),
                AgentTools.GetPendingCampaigns  => await Tool_GetPendingCampaigns(ct),
                AgentTools.SubmitFailoverRec    => await Tool_SubmitFailoverRecommendation(toolUse.Input, ct),
                AgentTools.OpenIncident         => await Tool_OpenIncident(toolUse.Input, ct),
                AgentTools.ResolveIncident      => await Tool_ResolveIncident(toolUse.Input, ct),
                AgentTools.HoldCampaign         => await Tool_HoldCampaign(toolUse.Input, ct),
                AgentTools.ReleaseCampaign      => await Tool_ReleaseCampaign(toolUse.Input, ct),
                _ => $"{{\"error\": \"Unknown tool: {toolUse.Name}\"}}"
            };

            actionsTaken.Add(new { tool = toolUse.Name, input = toolUse.Input, result });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} threw exception", toolUse.Name);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    // ─── Tool Implementations ─────────────────────────────────────────────────

    private async Task<string> Tool_CheckExternalStatus(JsonElement input, CancellationToken ct)
    {
        var providerStr = input.TryGetProperty("provider", out var p) ? p.GetString() : null;
        var results = new List<object>();

        var providers = string.IsNullOrEmpty(providerStr)
            ? _probes
            : _probes.Where(pr => pr.Provider.ToString().Equals(providerStr, StringComparison.OrdinalIgnoreCase));

        foreach (var probe in providers)
        {
            var config = await _db.ServiceConfigs
                .FirstOrDefaultAsync(s => s.Provider == probe.Provider && s.IsEnabled, ct);
            if (config == null) continue;

            var result = await probe.CheckExternalStatusAsync(config, ct);
            results.Add(new
            {
                provider = probe.Provider.ToString(),
                status = result.Status.ToString(),
                latency_ms = result.LatencyMs,
                error_code = result.ErrorCode,
                error_message = result.ErrorMessage,
                is_simulated = result.Detail?.ContainsKey("simulated") == true
            });
        }

        return JsonSerializer.Serialize(results);
    }

    private async Task<string> Tool_RunInternalProbe(JsonElement input, CancellationToken ct)
    {
        var providerStr = input.TryGetProperty("provider", out var p) ? p.GetString() : null;
        var results = new List<object>();

        var providers = string.IsNullOrEmpty(providerStr)
            ? _probes
            : _probes.Where(pr => pr.Provider.ToString().Equals(providerStr, StringComparison.OrdinalIgnoreCase));

        foreach (var probe in providers)
        {
            var config = await _db.ServiceConfigs
                .FirstOrDefaultAsync(s => s.Provider == probe.Provider && s.IsEnabled, ct);
            if (config == null) continue;

            var result = await probe.RunInternalProbeAsync(config, ct);

            // Persist the result
            var hcr = new HealthCheckResult
            {
                Id = Guid.NewGuid(),
                ServiceConfigId = config.Id,
                CheckedAt = DateTime.UtcNow,
                Status = result.Status,
                LatencyMs = result.LatencyMs,
                SuccessRate = result.SuccessRate,
                ErrorRate = result.ErrorRate,
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage,
                InternalStatus = result.Status,
                ProbeDetailJson = JsonDocument.Parse(JsonSerializer.Serialize(result.Detail ?? new())),
                IsSimulated = result.Detail?.ContainsKey("simulated") == true,
                SimulationScenario = result.Detail?.TryGetValue("scenario", out var sc) == true ? sc?.ToString() : null
            };
            _db.HealthCheckResults.Add(hcr);

            results.Add(new
            {
                provider = probe.Provider.ToString(),
                status = result.Status.ToString(),
                latency_ms = result.LatencyMs,
                success_rate = result.SuccessRate,
                error_rate = result.ErrorRate,
                error_code = result.ErrorCode,
                error_message = result.ErrorMessage,
                is_simulated = hcr.IsSimulated
            });
        }

        await _db.SaveChangesAsync(ct);
        return JsonSerializer.Serialize(results);
    }

    private async Task<string> Tool_GetHealthHistory(JsonElement input, CancellationToken ct)
    {
        var providerStr = input.TryGetProperty("provider", out var p) ? p.GetString() : null;
        var minutes = input.TryGetProperty("minutes", out var m) ? m.GetInt32() : 15;

        var since = DateTime.UtcNow.AddMinutes(-minutes);
        var query = _db.HealthCheckResults
            .Include(h => h.ServiceConfig)
            .Where(h => h.CheckedAt >= since)
            .OrderByDescending(h => h.CheckedAt);

        if (!string.IsNullOrEmpty(providerStr) &&
            Enum.TryParse<ServiceProvider>(providerStr, true, out var pEnum))
        {
            query = (IOrderedQueryable<HealthCheckResult>)query
                .Where(h => h.ServiceConfig!.Provider == pEnum);
        }

        var results = await query.Take(50).ToListAsync(ct);

        return JsonSerializer.Serialize(results.Select(r => new
        {
            provider = r.ServiceConfig?.Provider.ToString(),
            checked_at = r.CheckedAt,
            status = r.Status.ToString(),
            latency_ms = r.LatencyMs,
            success_rate = r.SuccessRate,
            error_rate = r.ErrorRate,
            error_code = r.ErrorCode
        }));
    }

    private async Task<string> Tool_GetOpenIncidents(CancellationToken ct)
    {
        var incidents = await _db.Incidents
            .Include(i => i.ServiceConfig)
            .Where(i => i.Status != IncidentStatus.Resolved)
            .OrderByDescending(i => i.OpenedAt)
            .Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(incidents.Select(i => new
        {
            id = i.Id,
            provider = i.ServiceConfig?.Provider.ToString(),
            title = i.Title,
            severity = i.Severity.ToString(),
            status = i.Status.ToString(),
            opened_at = i.OpenedAt,
            work_plan = i.WorkPlan
        }));
    }

    private async Task<string> Tool_GetRoutingState(CancellationToken ct)
    {
        var routes = await _routing.GetAllRoutesAsync(ct);
        return JsonSerializer.Serialize(routes.Select(r => new
        {
            service_type = r.ServiceType.ToString(),
            active_provider = r.ActiveServiceConfig?.Provider.ToString(),
            is_primary = r.ActiveServiceConfig?.IsPrimary,
            action = r.Action.ToString(),
            changed_at = r.ChangedAt
        }));
    }

    private async Task<string> Tool_GetPendingCampaigns(CancellationToken ct)
    {
        var campaigns = await _db.Campaigns
            .Where(c => c.GateStatus == CampaignGateStatus.Go && c.ScheduledAt > DateTime.UtcNow)
            .OrderBy(c => c.ScheduledAt)
            .Take(10)
            .ToListAsync(ct);

        return JsonSerializer.Serialize(campaigns.Select(c => new
        {
            id = c.Id,
            name = c.Name,
            artist = c.ArtistName,
            channels = c.ServiceTypes.Select(s => s.ToString()),
            scheduled_at = c.ScheduledAt
        }));
    }

    private async Task<string> Tool_SubmitFailoverRecommendation(JsonElement input, CancellationToken ct)
    {
        var serviceTypeStr = input.GetProperty("service_type").GetString()!;
        var toProviderStr = input.GetProperty("to_provider").GetString()!;
        var recommendation = input.GetProperty("recommendation").GetString()!;
        var workPlan = input.GetProperty("work_plan").GetString()!;

        var serviceType = Enum.Parse<ServiceType>(serviceTypeStr, true);
        var toProvider = Enum.Parse<ServiceProvider>(toProviderStr, true);
        var currentRoute = await _routing.GetActiveRouteAsync(serviceType, ct);

        var placeholderDecisionId = Guid.NewGuid();
        var approval = new FailoverApproval
        {
            Id = Guid.NewGuid(),
            AgentDecisionId = placeholderDecisionId, // Will be updated after decision is persisted
            ServiceType = serviceType,
            FromProvider = currentRoute.ActiveServiceConfig!.Provider,
            ToProvider = toProvider,
            AgentRecommendation = recommendation,
            WorkPlan = workPlan,
            Status = "pending",
            RequestedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _db.FailoverApprovals.Add(approval);
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning("FAILOVER RECOMMENDATION submitted: {From} → {To} for {ServiceType}",
            currentRoute.ActiveServiceConfig.Provider, toProvider, serviceType);

        return JsonSerializer.Serialize(new
        {
            approval_id = approval.Id,
            status = "pending_human_approval",
            from_provider = currentRoute.ActiveServiceConfig.Provider.ToString(),
            to_provider = toProvider.ToString(),
            expires_at = approval.ExpiresAt,
            message = "Failover recommendation submitted. Awaiting human operator approval."
        });
    }

    private async Task<string> Tool_OpenIncident(JsonElement input, CancellationToken ct)
    {
        var providerStr = input.GetProperty("provider").GetString()!;
        var title = input.GetProperty("title").GetString()!;
        var description = input.TryGetProperty("description", out var d) ? d.GetString() : null;
        var severityStr = input.GetProperty("severity").GetString()!;
        var workPlan = input.TryGetProperty("work_plan", out var wp) ? wp.GetString() : null;

        var provider = Enum.Parse<ServiceProvider>(providerStr, true);
        var severity = Enum.Parse<IncidentSeverity>(severityStr, true);

        var config = await _db.ServiceConfigs.FirstOrDefaultAsync(s => s.Provider == provider, ct);
        if (config == null) return JsonSerializer.Serialize(new { error = $"Provider {providerStr} not found" });

        var incident = new Incident
        {
            Id = Guid.NewGuid(),
            ServiceConfigId = config.Id,
            Title = title,
            Description = description,
            Severity = severity,
            Status = IncidentStatus.Open,
            OpenedAt = DateTime.UtcNow,
            WorkPlan = workPlan,
            IsSimulated = false
        };

        _db.Incidents.Add(incident);
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { incident_id = incident.Id, status = "open", title });
    }

    private async Task<string> Tool_ResolveIncident(JsonElement input, CancellationToken ct)
    {
        var incidentId = Guid.Parse(input.GetProperty("incident_id").GetString()!);
        var resolution = input.TryGetProperty("resolution", out var r) ? r.GetString() : "Resolved by agent";

        var incident = await _db.Incidents.FindAsync(new object[] { incidentId }, ct);
        if (incident == null) return JsonSerializer.Serialize(new { error = "Incident not found" });

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedAt = DateTime.UtcNow;
        _db.IncidentUpdates.Add(new IncidentUpdate
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            Message = resolution ?? "Resolved",
            Author = "agent",
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        return JsonSerializer.Serialize(new { incident_id = incidentId, status = "resolved" });
    }

    private async Task<string> Tool_HoldCampaign(JsonElement input, CancellationToken ct)
    {
        var campaignId = Guid.Parse(input.GetProperty("campaign_id").GetString()!);
        var reason = input.GetProperty("reason").GetString()!;

        var campaign = await _db.Campaigns.FindAsync(new object[] { campaignId }, ct);
        if (campaign == null) return JsonSerializer.Serialize(new { error = "Campaign not found" });

        campaign.GateStatus = CampaignGateStatus.Hold;
        campaign.HoldReason = reason;
        campaign.GateCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { campaign_id = campaignId, status = "held", reason });
    }

    private async Task<string> Tool_ReleaseCampaign(JsonElement input, CancellationToken ct)
    {
        var campaignId = Guid.Parse(input.GetProperty("campaign_id").GetString()!);
        var note = input.TryGetProperty("note", out var n) ? n.GetString() : "Released by agent";

        var campaign = await _db.Campaigns.FindAsync(new object[] { campaignId }, ct);
        if (campaign == null) return JsonSerializer.Serialize(new { error = "Campaign not found" });

        campaign.GateStatus = CampaignGateStatus.Go;
        campaign.HoldReason = null;
        campaign.GateCheckedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return JsonSerializer.Serialize(new { campaign_id = campaignId, status = "released", note });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private async Task<string> BuildInputSummaryAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Current UTC time: {DateTime.UtcNow:O}");

        var routes = await _routing.GetAllRoutesAsync(ct);
        sb.AppendLine("\n## Current Routing State");
        foreach (var r in routes)
            sb.AppendLine($"- {r.ServiceType}: active={r.ActiveServiceConfig?.Provider} (primary={r.ActiveServiceConfig?.IsPrimary})");

        var openIncidents = await _db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved)
            .CountAsync(ct);
        sb.AppendLine($"\n## Open Incidents: {openIncidents}");

        var pendingCampaigns = await _db.Campaigns
            .Where(c => c.GateStatus == CampaignGateStatus.Go && c.ScheduledAt > DateTime.UtcNow)
            .CountAsync(ct);
        sb.AppendLine($"## Campaigns pending launch: {pendingCampaigns}");

        return sb.ToString();
    }

    private static string? ExtractWorkPlan(string? reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning)) return null;
        // Look for "WORK PLAN" or "Work Plan" section headers
        var idx = reasoning.IndexOf("Work Plan", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = reasoning.IndexOf("WORK PLAN", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? reasoning[idx..].Trim() : reasoning.Trim();
    }

    private static string ExtractDecision(string? reasoning)
    {
        if (string.IsNullOrWhiteSpace(reasoning)) return "no_action";
        reasoning = reasoning.ToLower();
        if (reasoning.Contains("recommend failover") || reasoning.Contains("failover recommendation"))
            return "recommend_failover";
        if (reasoning.Contains("hold campaign") || reasoning.Contains("holding campaign"))
            return "hold_campaign";
        if (reasoning.Contains("reroute") || reasoning.Contains("re-route"))
            return "reroute_campaign";
        if (reasoning.Contains("open incident") || reasoning.Contains("opening incident"))
            return "open_incident";
        if (reasoning.Contains("all services") && reasoning.Contains("operational"))
            return "no_action";
        return "no_action";
    }

    private static List<ChannelGateResult> ParseChannelResults(AgentDecision decision, Campaign campaign)
    {
        return campaign.ServiceTypes.Select(serviceType =>
        {
            var isHeld = decision.Decision is "hold_campaign" or "recommend_failover";
            return new ChannelGateResult(
                serviceType,
                serviceType == ServiceType.Email ? ServiceProvider.Mailgun : ServiceProvider.Twilio,
                null,
                isHeld ? CampaignGateStatus.Hold : CampaignGateStatus.Go,
                isHeld ? (decision.WorkPlan ?? "Service degradation detected") : "All clear"
            );
        }).ToList();
    }
}
