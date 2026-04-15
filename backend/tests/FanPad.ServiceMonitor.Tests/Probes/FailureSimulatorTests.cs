using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Infrastructure.Probes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FanPad.ServiceMonitor.Tests.Probes;

/// <summary>
/// Tests for FailureSimulatorService — ensures every scenario returns correct state
/// and that provider-specific targeting works correctly.
/// </summary>
public class FailureSimulatorTests
{
    private readonly FailureSimulatorService _simulator;

    public FailureSimulatorTests()
    {
        _simulator = new FailureSimulatorService(NullLogger<FailureSimulatorService>.Instance);
    }

    // ── Default state ─────────────────────────────────────────────────────────

    [Fact]
    public void WhenNoScenario_IsSimulationActive_IsFalse()
    {
        _simulator.IsSimulationActive.Should().BeFalse();
        _simulator.ActiveScenario.Should().Be(FailureScenario.None);
    }

    [Fact]
    public void WhenNoScenario_GetSimulatedState_ReturnsNull()
    {
        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        state.Should().BeNull();
    }

    // ── Email scenarios ───────────────────────────────────────────────────────

    [Fact]
    public void MailgunCompleteOutage_ReturnsOutageForMailgun_NullForOthers()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);

        var mailgun = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        var ses     = _simulator.GetSimulatedState(ServiceProvider.Ses,     ProbeSource.InternalSyntheticProbe);
        var twilio  = _simulator.GetSimulatedState(ServiceProvider.Twilio,  ProbeSource.InternalSyntheticProbe);

        mailgun.Should().NotBeNull();
        mailgun!.Status.Should().Be(HealthStatus.MajorOutage);
        mailgun.SuccessRate.Should().Be(0);
        ses.Should().BeNull();
        twilio.Should().BeNull();
    }

    [Fact]
    public void MailgunPartialDegradation_ReturnsCorrectMetrics()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunPartialDegradation);

        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        state.Should().NotBeNull();
        state!.Status.Should().Be(HealthStatus.Degraded);
        state.LatencyMs.Should().Be(4800);
        state.SuccessRate.Should().Be(62m);
        state.ErrorRate.Should().Be(38m);
        state.ErrorCode.Should().Be("SMTP_TIMEOUT");
    }

    [Fact]
    public void MailgunHighLatency_ReturnsHighLatencyButGoodSuccessRate()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunHighLatency);
        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        state!.LatencyMs.Should().BeGreaterThan(5000);
        state.SuccessRate.Should().BeGreaterThanOrEqualTo(99m);
    }

    [Fact]
    public void MailgunHighErrorRate_ReturnsHighErrorWithLowLatency()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunHighErrorRate);
        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        state!.ErrorRate.Should().BeGreaterThan(20m);
        state.LatencyMs.Should().BeLessThan(500);
    }

    [Fact]
    public void BothEmailProvidersDown_ReturnsMajorOutageForBothMailgunAndSes()
    {
        _simulator.ActivateScenario(FailureScenario.BothEmailProvidersDown);

        var mailgun = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        var ses     = _simulator.GetSimulatedState(ServiceProvider.Ses,     ProbeSource.InternalSyntheticProbe);
        var twilio  = _simulator.GetSimulatedState(ServiceProvider.Twilio,  ProbeSource.InternalSyntheticProbe);

        mailgun!.Status.Should().Be(HealthStatus.MajorOutage);
        ses!.Status.Should().Be(HealthStatus.MajorOutage);
        twilio.Should().BeNull(); // SMS unaffected
    }

    [Fact]
    public void SesCompleteOutage_AffectsOnlySes()
    {
        _simulator.ActivateScenario(FailureScenario.SesCompleteOutage);

        _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe)!
                  .Status.Should().Be(HealthStatus.MajorOutage);
        _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe)
                  .Should().BeNull();
    }

    // ── SMS scenarios ─────────────────────────────────────────────────────────

    [Fact]
    public void TwilioCompleteOutage_AffectsOnlyTwilio()
    {
        _simulator.ActivateScenario(FailureScenario.TwilioCompleteOutage);

        var twilio  = _simulator.GetSimulatedState(ServiceProvider.Twilio,  ProbeSource.InternalSyntheticProbe);
        var mailgun = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        twilio!.Status.Should().Be(HealthStatus.MajorOutage);
        mailgun.Should().BeNull();
    }

    [Fact]
    public void TwilioPartialDegradation_Returns68PercentSuccess()
    {
        _simulator.ActivateScenario(FailureScenario.TwilioPartialDegradation);
        var state = _simulator.GetSimulatedState(ServiceProvider.Twilio, ProbeSource.InternalSyntheticProbe);

        state!.SuccessRate.Should().Be(68m);
        state.ErrorCode.Should().Be("CARRIER_ISSUES");
    }

    // ── Complex / cascade scenarios ───────────────────────────────────────────

    [Fact]
    public void MailgunThenSesFailure_SesFailsAfterThreeCallsOnly()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunThenSesFailure);

        // Mailgun always down
        var mailgun1 = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        mailgun1!.Status.Should().Be(HealthStatus.MajorOutage);

        // SES initially OK (call counts 2, 3)
        var ses1 = _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);
        var ses2 = _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);
        ses1.Should().BeNull();
        ses2.Should().BeNull();

        // After >3 total calls, SES also fails
        // (callCount was: 1=mailgun, 2=ses, 3=ses, now 4th call > 3)
        var ses3 = _simulator.GetSimulatedState(ServiceProvider.Ses, ProbeSource.InternalSyntheticProbe);
        ses3.Should().NotBeNull();
        ses3!.Status.Should().Be(HealthStatus.MajorOutage);
    }

    [Fact]
    public void IntermittentMailgunErrors_FailsOnEveryThirdCall()
    {
        _simulator.ActivateScenario(FailureScenario.IntermittentMailgunErrors);

        // Call 1: success (null = no simulation = healthy)
        var r1 = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        // Call 2: success
        var r2 = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);
        // Call 3: failure (3 % 3 == 0)
        var r3 = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        r1.Should().BeNull();
        r2.Should().BeNull();
        r3.Should().NotBeNull();
        r3!.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void AllServicesDown_AffectsAllProviders()
    {
        _simulator.ActivateScenario(FailureScenario.AllServicesDown);

        _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe)!
                  .Status.Should().Be(HealthStatus.MajorOutage);
        _simulator.GetSimulatedState(ServiceProvider.Ses,     ProbeSource.InternalSyntheticProbe)!
                  .Status.Should().Be(HealthStatus.MajorOutage);
        _simulator.GetSimulatedState(ServiceProvider.Twilio,  ProbeSource.InternalSyntheticProbe)!
                  .Status.Should().Be(HealthStatus.MajorOutage);
    }

    // ── Recovery scenarios ────────────────────────────────────────────────────

    [Fact]
    public void MailgunRecovering_Shows92PercentSuccessRate()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunRecovering);
        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe);

        state!.Status.Should().Be(HealthStatus.Degraded);
        state.SuccessRate.Should().Be(92m);
        state.ErrorCode.Should().Be("RECOVERING");
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearScenario_ResetsToNone()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);
        _simulator.IsSimulationActive.Should().BeTrue();

        _simulator.ClearScenario();

        _simulator.IsSimulationActive.Should().BeFalse();
        _simulator.ActiveScenario.Should().Be(FailureScenario.None);
        _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe).Should().BeNull();
    }

    [Fact]
    public void ActivatingNewScenario_ReplacesExistingOne()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);
        _simulator.ActivateScenario(FailureScenario.TwilioCompleteOutage);

        _simulator.ActiveScenario.Should().Be(FailureScenario.TwilioCompleteOutage);

        // Mailgun should now be null (twilio scenario)
        _simulator.GetSimulatedState(ServiceProvider.Mailgun, ProbeSource.InternalSyntheticProbe)
                  .Should().BeNull();
    }

    // ── Source discrimination ─────────────────────────────────────────────────

    [Theory]
    [InlineData(ProbeSource.InternalSyntheticProbe)]
    [InlineData(ProbeSource.ExternalStatusPage)]
    public void MailgunOutage_AffectsBothProbeSources(ProbeSource source)
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);
        var state = _simulator.GetSimulatedState(ServiceProvider.Mailgun, source);
        state.Should().NotBeNull();
        state!.Status.Should().Be(HealthStatus.MajorOutage);
    }
}
