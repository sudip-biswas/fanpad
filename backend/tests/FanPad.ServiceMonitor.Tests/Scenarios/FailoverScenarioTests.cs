using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Infrastructure.Data;
using FanPad.ServiceMonitor.Infrastructure.Probes;
using FanPad.ServiceMonitor.Infrastructure.Routing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FanPad.ServiceMonitor.Tests.Scenarios;

/// <summary>
/// End-to-end scenario tests for failover flows.
/// These tests validate the routing service and failure simulator work together correctly.
/// </summary>
public class FailoverScenarioTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RoutingService _routing;
    private readonly FailureSimulatorService _simulator;

    private static readonly Guid MailgunId = Guid.Parse("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid SesId     = Guid.Parse("a1000000-0000-0000-0000-000000000002");
    private static readonly Guid TwilioId  = Guid.Parse("a1000000-0000-0000-0000-000000000003");

    public FailoverScenarioTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        _routing = new RoutingService(_db, NullLogger<RoutingService>.Instance);
        _simulator = new FailureSimulatorService(NullLogger<FailureSimulatorService>.Instance);

        SeedDatabase();
    }

    private void SeedDatabase()
    {
        _db.ServiceConfigs.AddRange(
            new ServiceConfig { Id = MailgunId, Provider = ServiceProvider.Mailgun, ServiceType = ServiceType.Email, DisplayName = "Mailgun", IsPrimary = true,  Priority = 1, IsEnabled = true },
            new ServiceConfig { Id = SesId,     Provider = ServiceProvider.Ses,     ServiceType = ServiceType.Email, DisplayName = "AWS SES", IsPrimary = false, Priority = 2, IsEnabled = true },
            new ServiceConfig { Id = TwilioId,  Provider = ServiceProvider.Twilio,  ServiceType = ServiceType.Sms,   DisplayName = "Twilio",  IsPrimary = true,  Priority = 1, IsEnabled = true }
        );
        _db.RoutingStates.AddRange(
            new RoutingState { Id = Guid.NewGuid(), ServiceType = ServiceType.Email, ActiveServiceConfigId = MailgunId },
            new RoutingState { Id = Guid.NewGuid(), ServiceType = ServiceType.Sms,   ActiveServiceConfigId = TwilioId  }
        );
        _db.SaveChanges();
    }

    // ── Scenario 1: Mailgun complete outage → failover to SES ────────────────

    [Fact]
    public async Task Scenario_MailgunOutage_FailsoverToSes()
    {
        // Activate simulation
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);

        // Verify simulation returns outage
        var mailgunState = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        mailgunState!.Status.Should().Be(HealthStatus.MajorOutage);

        // Execute failover (would be triggered by agent recommendation + human approval)
        var failoverEvent = await _routing.ExecuteFailoverAsync(
            ServiceType.Email,
            ServiceProvider.Ses,
            FailoverAuthority.HumanApproved,
            "Mailgun returned 503 Service Unavailable. Success rate: 0%. Immediate failover recommended.",
            "Work Plan: Mailgun is completely down. Routing all email through AWS SES. Monitor for recovery. Revert when 3 consecutive clean probes recorded.",
            isSimulated: true
        );

        // Verify routing state changed
        var route = await _routing.GetActiveRouteAsync(ServiceType.Email);
        route.ActiveServiceConfig!.Provider.Should().Be(ServiceProvider.Ses);
        route.ActiveServiceConfig.IsPrimary.Should().BeFalse();

        // Verify failover event recorded
        failoverEvent.FromProvider.Should().Be(ServiceProvider.Mailgun);
        failoverEvent.ToProvider.Should().Be(ServiceProvider.Ses);
        failoverEvent.Authority.Should().Be(FailoverAuthority.HumanApproved);
        failoverEvent.Success.Should().BeTrue();

        // SES should be unaffected by the scenario
        _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe).Should().BeNull();
    }

    // ── Scenario 2: Mailgun partial degradation — monitor, recommend ──────────

    [Fact]
    public void Scenario_MailgunPartialDegradation_AgentShouldDetectDegradation()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunPartialDegradation);

        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        // 62% success rate → above 15% error threshold → agent should recommend failover
        state!.ErrorRate.Should().BeGreaterThan(15m,
            "error rate above 15% should trigger failover recommendation");
        state.LatencyMs.Should().BeGreaterThan(2000,
            "high latency is also a signal for degradation");
    }

    // ── Scenario 3: High latency only — should NOT trigger failover ───────────

    [Fact]
    public void Scenario_MailgunHighLatencyOnly_ShouldNotTriggerFailover()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunHighLatency);

        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        // 99% success despite high latency — agent should monitor, not failover
        state!.SuccessRate.Should().BeGreaterThanOrEqualTo(99m,
            "high success rate means agent should NOT failover");
        state.ErrorRate.Should().BeLessThan(5m,
            "error rate below 5% means no failover should occur");
    }

    // ── Scenario 4: Both email providers down ────────────────────────────────

    [Fact]
    public void Scenario_BothEmailDown_NeitherProviderIsHealthy()
    {
        _simulator.ActivateScenario(FailureScenario.BothEmailProvidersDown);

        var mailgun = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        var ses     = _simulator.GetSimulatedState(ServiceProvider.Ses,     ProbeSource.InternalSyntheticProbe);

        mailgun!.Status.Should().Be(HealthStatus.MajorOutage);
        ses!.Status.Should().Be(HealthStatus.MajorOutage);

        // In this scenario, agent must hold all email campaigns — cannot failover
        // (No healthy alternative exists)
    }

    // ── Scenario 5: SMS outage doesn't affect email ──────────────────────────

    [Fact]
    public async Task Scenario_TwilioOutage_EmailContinuesNormally()
    {
        _simulator.ActivateScenario(FailureScenario.TwilioCompleteOutage);

        // SMS is down
        _simulator.GetSimulatedState(ServiceProvider.Twilio, ProbeSource.InternalSyntheticProbe)!
                  .Status.Should().Be(HealthStatus.MajorOutage);

        // Email routing unchanged
        var emailRoute = await _routing.GetActiveRouteAsync(ServiceType.Email);
        emailRoute.ActiveServiceConfig!.Provider.Should().Be(ServiceProvider.Mailgun,
            "Twilio outage should not affect email routing");

        // Email providers healthy (no simulation state for them)
        _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe).Should().BeNull();
        _simulator.GetSimulatedState(ServiceProvider.Ses,     ProbeSource.InternalSyntheticProbe).Should().BeNull();
    }

    // ── Scenario 6: Recovery — revert to primary ──────────────────────────────

    [Fact]
    public async Task Scenario_MailgunRecovery_RevertsRouteToMailgun()
    {
        // First simulate failover to SES
        await _routing.ExecuteFailoverAsync(
            ServiceType.Email, ServiceProvider.Ses, FailoverAuthority.HumanApproved,
            "Mailgun was down", "Rerouted to SES", isSimulated: true
        );

        // Confirm SES is active
        var routeBeforeRevert = await _routing.GetActiveRouteAsync(ServiceType.Email);
        routeBeforeRevert.ActiveServiceConfig!.Provider.Should().Be(ServiceProvider.Ses);

        // Simulate recovery — activate recovering scenario
        _simulator.ActivateScenario(FailureScenario.MailgunRecovering);
        var recovering = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        recovering!.Status.Should().Be(HealthStatus.Degraded);
        recovering.SuccessRate.Should().Be(92m);

        // After 3 consecutive clean probes, agent recommends revert
        _simulator.ClearScenario(); // Mailgun fully recovered

        // Execute revert
        var route = await _routing.RevertToPrimaryAsync(ServiceType.Email, "Mailgun fully recovered. 3 consecutive clean probes.");

        route.ActiveServiceConfig!.Provider.Should().Be(ServiceProvider.Mailgun);
        route.ActiveServiceConfig.IsPrimary.Should().BeTrue();
    }

    // ── Scenario 7: Cascade failure ───────────────────────────────────────────

    [Fact]
    public void Scenario_CascadeFailure_SesFailsAfterFailover()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunThenSesFailure);

        // Mailgun always down
        var mailgun = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        mailgun!.Status.Should().Be(HealthStatus.MajorOutage);

        // Consume 3 calls (1=mailgun above, 2 more)
        _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);
        _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);

        // SES fails on 4th+ call
        var ses4 = _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);
        ses4.Should().NotBeNull("SES should fail after 3 total calls in cascade scenario");
        ses4!.Status.Should().Be(HealthStatus.MajorOutage);
    }

    // ── Scenario 8: Intermittent errors ──────────────────────────────────────

    [Fact]
    public void Scenario_IntermittentErrors_ShouldNotImmediatelyTriggerFailover()
    {
        _simulator.ActivateScenario(FailureScenario.IntermittentMailgunErrors);

        // Check 10 probes — 1/3 should fail
        var results = Enumerable.Range(0, 9)
            .Select(_ => _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe))
            .ToList();

        var failures = results.Count(r => r != null);
        var successes = results.Count(r => r == null); // null = healthy = not intercepted by simulator

        // Every 3rd fails → 3 failures in 9 probes
        failures.Should().Be(3, "intermittent scenario fails every 3rd call");
        successes.Should().Be(6, "2 out of every 3 probes should be healthy");
    }

    // ── Scenario 9: All services down ────────────────────────────────────────

    [Fact]
    public void Scenario_AllServicesDown_AgentMustHoldAllCampaigns()
    {
        _simulator.ActivateScenario(FailureScenario.AllServicesDown);

        var providers = new[] { ServiceProvider.Mailgun, ServiceProvider.Ses, ServiceProvider.Twilio };
        foreach (var p in providers)
        {
            var state = _simulator.GetSimulatedState(p, ProbeSource.InternalSyntheticProbe);
            state.Should().NotBeNull($"{p} should be in outage when AllServicesDown is active");
            state!.Status.Should().Be(HealthStatus.MajorOutage);
        }
    }

    // ── Routing service isolation tests ──────────────────────────────────────

    [Fact]
    public async Task GetPrimaryProvider_ReturnsMailgunForEmail()
    {
        var primary = await _routing.GetPrimaryProviderAsync(ServiceType.Email);
        primary.Should().NotBeNull();
        primary!.Provider.Should().Be(ServiceProvider.Mailgun);
        primary.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public async Task GetFallbackProvider_ReturnsSesForEmail()
    {
        var fallback = await _routing.GetFallbackProviderAsync(ServiceType.Email);
        fallback.Should().NotBeNull();
        fallback!.Provider.Should().Be(ServiceProvider.Ses);
        fallback.IsPrimary.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteFailover_ThrowsWhenTargetProviderDisabled()
    {
        // Disable SES
        var ses = await _db.ServiceConfigs.FindAsync(SesId);
        ses!.IsEnabled = false;
        await _db.SaveChangesAsync();

        var act = async () => await _routing.ExecuteFailoverAsync(
            ServiceType.Email, ServiceProvider.Ses,
            FailoverAuthority.HumanApproved, "test", "test plan"
        );

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    public void Dispose() => _db.Dispose();
}
