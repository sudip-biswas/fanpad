using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Models;

namespace FanPad.ServiceMonitor.Core.Interfaces;

public interface IRoutingService
{
    Task<RoutingState> GetActiveRouteAsync(ServiceType serviceType, CancellationToken ct = default);
    Task<IReadOnlyList<RoutingState>> GetAllRoutesAsync(CancellationToken ct = default);
    Task<FailoverEvent> ExecuteFailoverAsync(ServiceType serviceType, ServiceProvider toProvider,
        FailoverAuthority authority, string reason, string workPlan, Guid? incidentId = null,
        bool isSimulated = false, CancellationToken ct = default);
    Task<RoutingState> RevertToPrimaryAsync(ServiceType serviceType, string reason,
        CancellationToken ct = default);
    Task<ServiceConfig?> GetPrimaryProviderAsync(ServiceType serviceType, CancellationToken ct = default);
    Task<ServiceConfig?> GetFallbackProviderAsync(ServiceType serviceType, CancellationToken ct = default);
}
