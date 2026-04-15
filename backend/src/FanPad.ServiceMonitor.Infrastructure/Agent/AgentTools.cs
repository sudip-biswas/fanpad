using Anthropic.SDK.Messaging;
using System.Text.Json;

namespace FanPad.ServiceMonitor.Infrastructure.Agent;

/// <summary>
/// Defines all tools available to the Fanpad Health Agent.
/// </summary>
public static class AgentTools
{
    // Tool name constants — referenced in the dispatcher
    public const string CheckExternalStatus = "check_external_status";
    public const string RunInternalProbe    = "run_internal_probe";
    public const string GetHealthHistory    = "get_health_history";
    public const string GetOpenIncidents    = "get_open_incidents";
    public const string GetRoutingState     = "get_routing_state";
    public const string GetPendingCampaigns = "get_pending_campaigns";
    public const string SubmitFailoverRec   = "submit_failover_recommendation";
    public const string OpenIncident        = "open_incident";
    public const string ResolveIncident     = "resolve_incident";
    public const string HoldCampaign        = "hold_campaign";
    public const string ReleaseCampaign     = "release_campaign";

    public static List<Tool> GetAllTools() => new()
    {
        new Tool
        {
            Name = CheckExternalStatus,
            Description = """
                Fetches the official external status page for one or all messaging service providers.
                Returns current status indicator (operational, degraded, partial_outage, major_outage).
                Call this first to get a quick high-level view before running deeper internal probes.
                """,
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["provider"] = new Property
                    {
                        Type = "string",
                        Description = "Provider to check: 'mailgun', 'ses', 'twilio'. Omit to check all providers.",
                        Enum = new[] { "mailgun", "ses", "twilio" }
                    }
                },
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = RunInternalProbe,
            Description = """
                Runs a synthetic internal health probe against a test account for one or all providers.
                This performs an actual (simulated) test message send and measures latency and success rate.
                More accurate than status pages but takes slightly longer. Results are persisted to the database.
                Use this when you need to confirm whether a service can actually deliver messages.
                """,
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["provider"] = new Property
                    {
                        Type = "string",
                        Description = "Provider to probe: 'mailgun', 'ses', 'twilio'. Omit to probe all providers.",
                        Enum = new[] { "mailgun", "ses", "twilio" }
                    }
                },
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = GetHealthHistory,
            Description = """
                Returns recent health check results from the database for trend analysis.
                Use this to determine whether a service is improving or worsening over time.
                Critical for distinguishing transient blips from sustained degradation.
                """,
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["provider"] = new Property
                    {
                        Type = "string",
                        Description = "Filter by provider. Omit for all.",
                        Enum = new[] { "mailgun", "ses", "twilio" }
                    },
                    ["minutes"] = new Property
                    {
                        Type = "integer",
                        Description = "How many minutes of history to retrieve. Default: 15. Max: 60."
                    }
                },
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = GetOpenIncidents,
            Description = "Returns all currently open or monitoring incidents across all service providers.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>(),
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = GetRoutingState,
            Description = """
                Returns the current active routing configuration for all service types (email, sms).
                Shows whether each channel is using its primary provider or a fallback.
                """,
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>(),
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = GetPendingCampaigns,
            Description = "Returns upcoming campaigns that are currently gated as GO and scheduled to launch soon.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>(),
                Required = new List<string>()
            }
        },

        new Tool
        {
            Name = SubmitFailoverRec,
            Description = """
                Submits a failover recommendation for human operator review and approval.
                You MUST call this instead of executing failovers directly. The operator will approve or reject.
                Always include a clear work_plan written for a non-technical campaign manager.
                """,
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["service_type"] = new Property
                    {
                        Type = "string",
                        Description = "Service type to failover: 'email' or 'sms'",
                        Enum = new[] { "email", "sms" }
                    },
                    ["to_provider"] = new Property
                    {
                        Type = "string",
                        Description = "Target fallback provider to route to",
                        Enum = new[] { "mailgun", "ses", "twilio" }
                    },
                    ["recommendation"] = new Property
                    {
                        Type = "string",
                        Description = "Technical explanation of why failover is recommended, with evidence."
                    },
                    ["work_plan"] = new Property
                    {
                        Type = "string",
                        Description = """
                            Plain-English work plan for the operator/campaign manager. Include:
                            1. What happened (brief summary)
                            2. What will change (which provider traffic moves to)
                            3. Expected impact on in-flight campaigns
                            4. What to watch for
                            5. When/how to revert back to primary
                            """
                    }
                },
                Required = new List<string> { "service_type", "to_provider", "recommendation", "work_plan" }
            }
        },

        new Tool
        {
            Name = OpenIncident,
            Description = "Opens a new incident record for a service degradation or outage.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["provider"] = new Property
                    {
                        Type = "string",
                        Enum = new[] { "mailgun", "ses", "twilio" }
                    },
                    ["title"] = new Property
                    {
                        Type = "string",
                        Description = "Short descriptive title for the incident (max 200 chars)"
                    },
                    ["description"] = new Property
                    {
                        Type = "string",
                        Description = "Detailed description of observed symptoms"
                    },
                    ["severity"] = new Property
                    {
                        Type = "string",
                        Enum = new[] { "low", "medium", "high", "critical" }
                    },
                    ["work_plan"] = new Property
                    {
                        Type = "string",
                        Description = "Initial work plan / recommended actions"
                    }
                },
                Required = new List<string> { "provider", "title", "severity" }
            }
        },

        new Tool
        {
            Name = ResolveIncident,
            Description = "Marks an open incident as resolved after confirming service recovery.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["incident_id"] = new Property
                    {
                        Type = "string",
                        Description = "UUID of the incident to resolve"
                    },
                    ["resolution"] = new Property
                    {
                        Type = "string",
                        Description = "Description of how the issue was resolved"
                    }
                },
                Required = new List<string> { "incident_id" }
            }
        },

        new Tool
        {
            Name = HoldCampaign,
            Description = "Places a campaign on hold, preventing it from launching until explicitly released.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["campaign_id"] = new Property
                    {
                        Type = "string",
                        Description = "UUID of the campaign to hold"
                    },
                    ["reason"] = new Property
                    {
                        Type = "string",
                        Description = "Clear explanation for why the campaign is being held"
                    }
                },
                Required = new List<string> { "campaign_id", "reason" }
            }
        },

        new Tool
        {
            Name = ReleaseCampaign,
            Description = "Releases a held campaign back to GO status after service recovery.",
            InputSchema = new InputSchema
            {
                Type = "object",
                Properties = new Dictionary<string, Property>
                {
                    ["campaign_id"] = new Property
                    {
                        Type = "string",
                        Description = "UUID of the campaign to release"
                    },
                    ["note"] = new Property
                    {
                        Type = "string",
                        Description = "Optional note about why the campaign is being released"
                    }
                },
                Required = new List<string> { "campaign_id" }
            }
        }
    };
}
