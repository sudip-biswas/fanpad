using Microsoft.AspNetCore.SignalR;

namespace FanPad.ServiceMonitor.Api.Hubs;

/// <summary>
/// SignalR hub for pushing real-time service health updates to the Angular dashboard.
/// </summary>
public class ServiceStatusHub : Hub
{
    // Groups for targeted pushes
    public const string DashboardGroup = "dashboard";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, DashboardGroup);
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Typed hub events pushed to clients.
/// </summary>
public static class HubEvents
{
    public const string ServiceHealthUpdate   = "ServiceHealthUpdate";
    public const string IncidentOpened        = "IncidentOpened";
    public const string IncidentResolved      = "IncidentResolved";
    public const string FailoverRecommended   = "FailoverRecommended";
    public const string FailoverExecuted      = "FailoverExecuted";
    public const string CampaignGateChanged   = "CampaignGateChanged";
    public const string AgentDecisionLogged   = "AgentDecisionLogged";
    public const string SimulationActivated   = "SimulationActivated";
    public const string SimulationCleared     = "SimulationCleared";
}
