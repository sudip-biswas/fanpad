using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FanPad.ServiceMonitor.Api.BackgroundServices;

/// <summary>
/// Scheduled background service that triggers the agent health check every N minutes.
/// Strategy: runs every 2 minutes in normal operation (fast feedback loop).
/// When a degradation is detected, frequency increases to every 30 seconds.
/// </summary>
public class HealthMonitorBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IHubContext<ServiceStatusHub> _hub;
    private readonly ILogger<HealthMonitorBackgroundService> _logger;

    // Normal polling interval: 2 minutes
    private static readonly TimeSpan NormalInterval = TimeSpan.FromMinutes(2);
    // Elevated polling interval when degradation is detected: 30 seconds
    private static readonly TimeSpan ElevatedInterval = TimeSpan.FromSeconds(30);

    private bool _degradationDetected = false;

    public HealthMonitorBackgroundService(
        IServiceProvider services,
        IHubContext<ServiceStatusHub> hub,
        ILogger<HealthMonitorBackgroundService> logger)
    {
        _services = services;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Health Monitor background service started. Poll interval: {Interval}", NormalInterval);

        // Initial delay to let the app fully start
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health monitor cycle threw an unhandled exception");
            }

            var interval = _degradationDetected ? ElevatedInterval : NormalInterval;
            _logger.LogDebug("Next health check in {Interval} ({Mode} mode)",
                interval, _degradationDetected ? "ELEVATED" : "normal");

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("Health Monitor background service stopped.");
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        // Create a scoped container so we get fresh DbContext per cycle
        await using var scope = _services.CreateAsyncScope();
        var agent = scope.ServiceProvider.GetRequiredService<IAgentOrchestrationService>();

        _logger.LogDebug("Running scheduled health check cycle...");

        var decision = await agent.RunScheduledHealthCheckAsync(ct);

        // Determine if we should elevate polling frequency
        _degradationDetected = decision.Decision is "recommend_failover" or "open_incident" or "hold_campaign";

        // Push to dashboard clients via SignalR
        await _hub.Clients.Group(ServiceStatusHub.DashboardGroup)
            .SendAsync(HubEvents.AgentDecisionLogged, new
            {
                id = decision.Id,
                decision = decision.Decision,
                workPlan = decision.WorkPlan,
                decidedAt = decision.DecidedAt,
                durationMs = decision.DurationMs
            }, ct);

        if (_degradationDetected)
        {
            _logger.LogWarning("Degradation detected ({Decision}). Elevating poll frequency to {Interval}",
                decision.Decision, ElevatedInterval);
        }
    }
}
