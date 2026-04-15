using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
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
        Structure it exactly like the examples below. Do not use technical jargon, error codes, or stack traces.
        Label the section "Work Plan:" so the system can extract it.

        EXAMPLE WORK PLAN — Complete outage, failover recommended:
        ---
        Work Plan:
        What happened: Mailgun is completely unreachable as of 14:32 UTC. Our internal test send
        failed with zero delivery, and Mailgun's own status page confirms a service outage.

        What we're doing: We're requesting your approval to route all outbound email through
        AWS SES (our backup provider) until Mailgun recovers.

        What you need to do: Review this recommendation in the Routing tab and click
        "Approve Failover." This takes about 5 seconds to complete.

        Impact on campaigns: The "Summer Tour Announcement" email campaign (scheduled 16:00 UTC)
        will be held until the failover is approved. Once approved, it will send normally through SES.
        No messages will be lost — they'll be delayed by however long approval takes.

        When to revert: We'll notify you when Mailgun has passed 3 consecutive health checks.
        Typically this takes 15–60 minutes after Mailgun resolves their incident.
        ---

        EXAMPLE WORK PLAN — Partial degradation, incident opened, monitoring:
        ---
        Work Plan:
        What happened: Mailgun is degraded — our probes show a 38% error rate and 4.8 second
        average delivery latency over the last 10 minutes. Mailgun's status page does not yet
        show an incident, but our internal data suggests real delivery problems.

        What we're doing: We've opened an incident and will continue monitoring closely.
        We are NOT recommending failover yet because some messages are still getting through.

        What you need to do: No action needed right now. Keep an eye on the Incidents tab.
        If the error rate climbs above 50% or stays above 15% for another 10 minutes,
        we'll escalate to a failover recommendation automatically.

        Impact on campaigns: Campaigns may experience delayed delivery. We recommend
        postponing any high-priority launches by 30 minutes while we monitor.
        ---

        EXAMPLE WORK PLAN — Both email providers down, hold all campaigns:
        ---
        Work Plan:
        What happened: Both Mailgun and AWS SES are currently unreachable. This is an
        unusual situation — we have no healthy email provider to route through.

        What we're doing: All email campaigns have been placed on hold automatically.
        SMS campaigns are unaffected and proceeding normally.

        What you need to do: Do not approve any email failover — there is nowhere to route.
        Monitor the Incidents tab. Notify affected artists that email sends are paused.
        We will alert you the moment either provider recovers.

        Impact: All outbound email is paused. SMS-only campaigns continue normally.
        Estimated duration: unknown — this depends on external provider recovery.
        ---

        EXAMPLE WORK PLAN — Recovery detected, revert recommended:
        ---
        Work Plan:
        What happened: Mailgun (our primary email provider) was in outage. We routed
        email through AWS SES as a fallback.

        Good news: Mailgun has now passed 3 consecutive health checks with 100% delivery
        and normal latency (under 300ms). The outage appears fully resolved.

        What we're doing: Recommending that you revert email routing back to Mailgun.

        What you need to do: Click "Approve Revert" or use the "Revert to Primary" button
        in the Routing tab. This is optional — SES will continue to work if you prefer
        to wait longer before switching back.

        Impact: Campaigns will resume through Mailgun. No messages will be affected —
        the switch is seamless.
        ---

        REVERT TO PRIMARY:
        - After 3 consecutive clean probes on the original primary, recommend revert.
        - Clearly state the improvement trend in your recommendation.
        """;

    public AgentOrchestrationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IEnumerable<IHealthProbeService> probes,
        IRoutingService routing,
        AppDbContext db,
        ILogger<AgentOrchestrationService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("anthropic");
        _apiKey = configuration["Anthropic:ApiKey"]
            ?? throw new InvalidOperationException("Anthropic:ApiKey is required");
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

        // Build tools JSON manually to guarantee correct lowercase JSON Schema field names.
        // Serializing SDK types directly can produce PascalCase keys (e.g. "Enum", "Type")
        // which are rejected by Anthropic's JSON Schema draft 2020-12 validator.
        var toolsArray = new JsonArray();
        foreach (var tool in AgentTools.GetAllTools())
        {
            var schema = tool.InputSchema;
            var propsObj = new JsonObject();
            if (schema.Properties != null)
            {
                foreach (var kvp in schema.Properties)
                {
                    var prop = kvp.Value;
                    var propObj = new JsonObject();
                    if (!string.IsNullOrEmpty(prop.Type)) propObj["type"] = prop.Type;
                    if (!string.IsNullOrEmpty(prop.Description)) propObj["description"] = prop.Description;
                    if (prop.Enum is { Length: > 0 })
                    {
                        var enumArr = new JsonArray();
                        foreach (var e in prop.Enum) enumArr.Add((JsonNode)e);
                        propObj["enum"] = enumArr;
                    }
                    propsObj[kvp.Key] = propObj;
                }
            }
            var schemaObj = new JsonObject { ["type"] = "object", ["properties"] = propsObj };
            if (schema.Required is { Count: > 0 })
            {
                var reqArr = new JsonArray();
                foreach (var r in schema.Required) reqArr.Add((JsonNode)r);
                schemaObj["required"] = reqArr;
            }
            toolsArray.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = schemaObj
            });
        }
        var toolsNode = (JsonNode)toolsArray;

        // Messages kept as JsonNodes to avoid SDK type coupling
        var messages = new List<JsonNode>
        {
            new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = campaignId.HasValue
                            ? $"Please evaluate the campaign gate for campaign ID {campaignId}. Context:\n{inputSummary}"
                            : $"Please perform a full service health evaluation. Context:\n{inputSummary}"
                    }
                }
            }
        };

        string? finalDecision = null;
        string? finalReasoning = null;
        string? workPlan = null;
        int promptTokens = 0;
        int completionTokens = 0;

        // Agentic loop — continue until the model stops using tools
        for (var iteration = 0; iteration < 10; iteration++)
        {
            var requestBody = new JsonObject
            {
                ["model"] = "claude-sonnet-4-6",
                ["max_tokens"] = 4096,
                ["system"] = SystemPrompt,
                ["messages"] = new JsonArray(messages.Select(m => m.DeepClone()).ToArray()),
                ["tools"] = toolsNode!.DeepClone()
            };

            using var httpResp = await CallAnthropicAsync(requestBody, ct);
            using var doc = await JsonDocument.ParseAsync(
                await httpResp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;

            if (root.TryGetProperty("usage", out var usage))
            {
                promptTokens += usage.TryGetProperty("input_tokens", out var it) ? it.GetInt32() : 0;
                completionTokens += usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt32() : 0;
            }

            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
            var content = root.GetProperty("content");

            // Collect text reasoning
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var bt) && bt.GetString() == "text"
                    && block.TryGetProperty("text", out var txt)
                    && !string.IsNullOrWhiteSpace(txt.GetString()))
                {
                    finalReasoning = (finalReasoning ?? "") + txt.GetString() + "\n";
                }
            }

            if (stopReason == "end_turn")
            {
                workPlan = ExtractWorkPlan(finalReasoning);
                finalDecision = ExtractDecision(finalReasoning);
                break;
            }

            if (stopReason == "tool_use")
            {
                // Add assistant turn — preserve the full content array as-is
                messages.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = JsonNode.Parse(content.GetRawText())
                });

                // Process tool calls
                var toolResults = new JsonArray();
                foreach (var block in content.EnumerateArray())
                {
                    if (!block.TryGetProperty("type", out var typeProp)
                        || typeProp.GetString() != "tool_use") continue;

                    var toolId   = block.GetProperty("id").GetString()!;
                    var toolName = block.GetProperty("name").GetString()!;
                    var toolInput = block.GetProperty("input");

                    var result = await ExecuteToolAsync(toolId, toolName, toolInput, actionsTaken, ct);
                    toolResults.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolId,
                        ["content"] = result
                    });
                }

                // Add user turn with tool results
                messages.Add(new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = toolResults
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
            ModelUsed = "claude-sonnet-4-6",
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            DecidedAt = DateTime.UtcNow,
            DurationMs = (int)sw.ElapsedMilliseconds
        };

        _db.AgentDecisions.Add(decision);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Agent decision complete: {Decision} in {DurationMs}ms | tokens: {Tokens}",
            decision.Decision, decision.DurationMs, promptTokens + completionTokens);

        return decision;
    }

    // ─── Anthropic HTTP Call ──────────────────────────────────────────────────

    private async Task<HttpResponseMessage> CallAnthropicAsync(JsonObject body, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Anthropic API error {Status}: {Body}", response.StatusCode, errBody);
            response.EnsureSuccessStatusCode(); // throws
        }

        return response;
    }

    // ─── Tool Execution Dispatcher ────────────────────────────────────────────

    private async Task<string> ExecuteToolAsync(
        string toolId, string toolName, JsonElement toolInput,
        List<object> actionsTaken, CancellationToken ct)
    {
        _logger.LogDebug("Agent tool call: {ToolName} | id: {ToolId}", toolName, toolId);

        try
        {
            var result = toolName switch
            {
                AgentTools.CheckExternalStatus  => await Tool_CheckExternalStatus(toolInput, ct),
                AgentTools.RunInternalProbe     => await Tool_RunInternalProbe(toolInput, ct),
                AgentTools.GetHealthHistory     => await Tool_GetHealthHistory(toolInput, ct),
                AgentTools.GetOpenIncidents     => await Tool_GetOpenIncidents(ct),
                AgentTools.GetRoutingState      => await Tool_GetRoutingState(ct),
                AgentTools.GetPendingCampaigns  => await Tool_GetPendingCampaigns(ct),
                AgentTools.SubmitFailoverRec    => await Tool_SubmitFailoverRecommendation(toolInput, ct),
                AgentTools.OpenIncident         => await Tool_OpenIncident(toolInput, ct),
                AgentTools.ResolveIncident      => await Tool_ResolveIncident(toolInput, ct),
                AgentTools.HoldCampaign         => await Tool_HoldCampaign(toolInput, ct),
                AgentTools.ReleaseCampaign      => await Tool_ReleaseCampaign(toolInput, ct),
                _ => $"{{\"error\": \"Unknown tool: {toolName}\"}}"
            };

            actionsTaken.Add(new { tool = toolName, result });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} threw exception", toolName);
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
