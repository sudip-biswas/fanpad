using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FanPad.ServiceMonitor.Infrastructure.Probes;

/// <summary>
/// Base class for all health probe services.
/// Handles failure simulation injection and provides timing utilities.
/// </summary>
public abstract class BaseProbeService : IHealthProbeService
{
    protected readonly IFailureSimulator FailureSimulator;
    protected readonly ILogger Logger;
    protected readonly HttpClient HttpClient;

    public abstract ServiceProvider Provider { get; }

    protected BaseProbeService(IFailureSimulator failureSimulator, ILogger logger, HttpClient httpClient)
    {
        FailureSimulator = failureSimulator;
        Logger = logger;
        HttpClient = httpClient;
    }

    public async Task<ProbeResult> RunInternalProbeAsync(ServiceConfig config, CancellationToken ct = default)
    {
        var simulated = FailureSimulator.GetSimulatedState(Provider, ProbeSource.InternalSyntheticProbe);
        if (simulated != null)
        {
            Logger.LogDebug("SIMULATION [{Provider}] Internal probe returning simulated state: {Status}", Provider, simulated.Status);
            return ToProbeResult(simulated, ProbeSource.InternalSyntheticProbe);
        }

        return await RunInternalProbeInternalAsync(config, ct);
    }

    public async Task<ProbeResult> CheckExternalStatusAsync(ServiceConfig config, CancellationToken ct = default)
    {
        var simulated = FailureSimulator.GetSimulatedState(Provider, ProbeSource.ExternalStatusPage);
        if (simulated != null)
        {
            Logger.LogDebug("SIMULATION [{Provider}] External status returning simulated state: {Status}", Provider, simulated.Status);
            return ToProbeResult(simulated, ProbeSource.ExternalStatusPage);
        }

        return await CheckExternalStatusInternalAsync(config, ct);
    }

    protected abstract Task<ProbeResult> RunInternalProbeInternalAsync(ServiceConfig config, CancellationToken ct);
    protected abstract Task<ProbeResult> CheckExternalStatusInternalAsync(ServiceConfig config, CancellationToken ct);

    protected async Task<(T Result, int ElapsedMs)> TimeAsync<T>(Func<Task<T>> fn)
    {
        var sw = Stopwatch.StartNew();
        var result = await fn();
        return (result, (int)sw.ElapsedMilliseconds);
    }

    protected ProbeResult SafeFallback(Exception ex, ProbeSource source)
    {
        Logger.LogError(ex, "[{Provider}] Probe threw exception", Provider);
        return ProbeResult.Unknown(source, $"Probe exception: {ex.Message}");
    }

    private static ProbeResult ToProbeResult(SimulatedProbeState s, ProbeSource source) =>
        new(source, s.Status, s.LatencyMs, s.SuccessRate, s.ErrorRate, s.ErrorCode, s.ErrorMessage,
            new Dictionary<string, object> { ["simulated"] = true, ["scenario"] = s.ScenarioName });
}
