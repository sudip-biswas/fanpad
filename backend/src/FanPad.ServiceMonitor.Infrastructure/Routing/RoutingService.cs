using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FanPad.ServiceMonitor.Infrastructure.Routing;

public class RoutingService : IRoutingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<RoutingService> _logger;

    public RoutingService(AppDbContext db, ILogger<RoutingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<RoutingState> GetActiveRouteAsync(ServiceType serviceType, CancellationToken ct = default) =>
        await _db.RoutingStates
            .Include(r => r.ActiveServiceConfig)
            .FirstOrDefaultAsync(r => r.ServiceType == serviceType, ct)
        ?? throw new InvalidOperationException($"No routing state found for {serviceType}");

    public async Task<IReadOnlyList<RoutingState>> GetAllRoutesAsync(CancellationToken ct = default) =>
        await _db.RoutingStates
            .Include(r => r.ActiveServiceConfig)
            .ToListAsync(ct);

    public async Task<FailoverEvent> ExecuteFailoverAsync(
        ServiceType serviceType,
        ServiceProvider toProvider,
        FailoverAuthority authority,
        string reason,
        string workPlan,
        Guid? incidentId = null,
        bool isSimulated = false,
        CancellationToken ct = default)
    {
        var currentRoute = await GetActiveRouteAsync(serviceType, ct);
        var fromProvider = currentRoute.ActiveServiceConfig!.Provider;

        if (fromProvider == toProvider)
        {
            _logger.LogWarning("Failover requested but {Provider} is already active for {ServiceType}", toProvider, serviceType);
        }

        // Find target service config
        var targetConfig = await _db.ServiceConfigs
            .FirstOrDefaultAsync(s => s.Provider == toProvider && s.ServiceType == serviceType && s.IsEnabled, ct)
            ?? throw new InvalidOperationException($"No enabled config found for provider {toProvider} on {serviceType}");

        // Update routing state
        currentRoute.ActiveServiceConfigId = targetConfig.Id;
        currentRoute.ActiveServiceConfig = targetConfig;
        currentRoute.Action = authority == FailoverAuthority.AgentAuto ? RoutingAction.Auto : RoutingAction.AgentRecommended;
        currentRoute.Reason = reason;
        currentRoute.ChangedAt = DateTime.UtcNow;
        currentRoute.ChangedBy = authority == FailoverAuthority.AgentAuto ? "agent" : "human";

        // Record failover event
        var failoverEvent = new FailoverEvent
        {
            Id = Guid.NewGuid(),
            ServiceType = serviceType,
            FromProvider = fromProvider,
            ToProvider = toProvider,
            IncidentId = incidentId,
            Authority = authority,
            AgentRecommendation = reason,
            WorkPlan = workPlan,
            ApprovedBy = authority == FailoverAuthority.HumanApproved ? "operator" : null,
            InitiatedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Success = true,
            IsSimulated = isSimulated
        };

        _db.FailoverEvents.Add(failoverEvent);
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "FAILOVER EXECUTED: {ServiceType} {From} → {To} | Authority: {Authority} | Reason: {Reason}",
            serviceType, fromProvider, toProvider, authority, reason);

        return failoverEvent;
    }

    public async Task<RoutingState> RevertToPrimaryAsync(ServiceType serviceType, string reason, CancellationToken ct = default)
    {
        var primaryConfig = await GetPrimaryProviderAsync(serviceType, ct)
            ?? throw new InvalidOperationException($"No primary config found for {serviceType}");

        var currentRoute = await GetActiveRouteAsync(serviceType, ct);
        currentRoute.ActiveServiceConfigId = primaryConfig.Id;
        currentRoute.ActiveServiceConfig = primaryConfig;
        currentRoute.Action = RoutingAction.Auto;
        currentRoute.Reason = reason;
        currentRoute.ChangedAt = DateTime.UtcNow;
        currentRoute.ChangedBy = "agent";

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("REVERTED to primary: {ServiceType} → {Provider} | Reason: {Reason}",
            serviceType, primaryConfig.Provider, reason);

        return currentRoute;
    }

    public async Task<ServiceConfig?> GetPrimaryProviderAsync(ServiceType serviceType, CancellationToken ct = default) =>
        await _db.ServiceConfigs
            .Where(s => s.ServiceType == serviceType && s.IsPrimary && s.IsEnabled)
            .OrderBy(s => s.Priority)
            .FirstOrDefaultAsync(ct);

    public async Task<ServiceConfig?> GetFallbackProviderAsync(ServiceType serviceType, CancellationToken ct = default) =>
        await _db.ServiceConfigs
            .Where(s => s.ServiceType == serviceType && !s.IsPrimary && s.IsEnabled)
            .OrderBy(s => s.Priority)
            .FirstOrDefaultAsync(ct);
}
