using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Core.Interfaces;

/// <summary>
/// Controls injected failure scenarios for demo and testing purposes.
/// When a scenario is active, health probes return simulated data
/// instead of calling real external APIs.
/// </summary>
public interface IFailureSimulator
{
    FailureScenario ActiveScenario { get; }
    bool IsSimulationActive { get; }

    void ActivateScenario(FailureScenario scenario);
    void ClearScenario();

    /// <summary>
    /// Returns simulated probe state for a given provider under the active scenario.
    /// Returns null if the provider is not affected by the scenario.
    /// </summary>
    SimulatedProbeState? GetSimulatedState(ServiceProvider provider, ProbeSource source);
}

public record SimulatedProbeState(
    HealthStatus Status,
    int? LatencyMs,
    decimal? SuccessRate,
    decimal? ErrorRate,
    string? ErrorCode,
    string? ErrorMessage,
    string ScenarioName
);
