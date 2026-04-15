namespace FanPad.ServiceMonitor.Core.Enums;

public enum ServiceType
{
    Email,
    Sms,
    Push
}

public enum ServiceProvider
{
    Mailgun,
    Ses,
    Twilio,
    Sns
}

public enum HealthStatus
{
    Operational,
    Degraded,
    PartialOutage,
    MajorOutage,
    Unknown
}

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum IncidentStatus
{
    Open,
    Monitoring,
    Resolved
}

public enum RoutingAction
{
    Auto,
    ManualOverride,
    AgentRecommended
}

public enum CampaignGateStatus
{
    Go,
    Hold,
    Rerouted,
    Cancelled
}

public enum FailoverAuthority
{
    AgentAuto,
    HumanApproved,
    HumanRejected
}

/// <summary>
/// All supported mock failure scenarios for the simulation controller.
/// </summary>
public enum FailureScenario
{
    None,

    // Email failures
    MailgunCompleteOutage,
    MailgunPartialDegradation,       // High latency, moderate error rate
    MailgunHighLatency,              // Latency spike only, no errors
    MailgunHighErrorRate,            // Errors only, latency fine
    SesCompleteOutage,
    SesPartialDegradation,
    BothEmailProvidersDown,          // Catastrophic: no email fallback

    // SMS failures
    TwilioCompleteOutage,
    TwilioPartialDegradation,
    TwilioHighLatency,

    // Recovery scenarios (used to simulate return to healthy state)
    MailgunRecovering,               // Improving metrics trending up
    TwilioRecovering,

    // Cascading / complex scenarios
    MailgunThenSesFailure,           // Mailgun fails, failover to SES, then SES also fails
    IntermittentMailgunErrors,       // Flapping - errors come and go
    AllServicesDown,                 // Full outage - all providers unhealthy
}

public enum AgentDecisionType
{
    NoAction,
    RecommendFailover,
    AutoFailover,
    HoldCampaign,
    ReleaseCampaign,
    RerouteCampaign,
    OpenIncident,
    UpdateIncident,
    ResolveIncident,
    RecommendRevert
}

public enum ProbeSource
{
    ExternalStatusPage,
    InternalSyntheticProbe
}
