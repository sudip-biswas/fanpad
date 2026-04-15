using FanPad.ServiceMonitor.Core.Enums;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Infrastructure.Probes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Xunit;

namespace FanPad.ServiceMonitor.Tests.Integration;

/// <summary>
/// Integration tests for probe services.
/// When simulation is active, probes return simulated data without real HTTP calls.
/// When simulation is inactive, probes use a mock HTTP handler.
/// </summary>
public class ProbeServiceIntegrationTests
{
    private readonly FailureSimulatorService _simulator;
    private readonly ServiceConfig _mailgunConfig;
    private readonly ServiceConfig _sesConfig;
    private readonly ServiceConfig _twilioConfig;

    public ProbeServiceIntegrationTests()
    {
        _simulator = new FailureSimulatorService(NullLogger<FailureSimulatorService>.Instance);

        _mailgunConfig = new ServiceConfig
        {
            Id = Guid.NewGuid(),
            Provider = ServiceProvider.Mailgun,
            ServiceType = ServiceType.Email,
            DisplayName = "Mailgun",
            ConfigJson = JsonDocument.Parse("""{"domain":"sandbox.mailgun.org","test_recipient":"test@test.com"}""")
        };
        _sesConfig = new ServiceConfig
        {
            Id = Guid.NewGuid(),
            Provider = ServiceProvider.Ses,
            ServiceType = ServiceType.Email,
            DisplayName = "AWS SES",
            ConfigJson = JsonDocument.Parse("""{"region":"us-east-1","test_recipient":"test@test.com"}""")
        };
        _twilioConfig = new ServiceConfig
        {
            Id = Guid.NewGuid(),
            Provider = ServiceProvider.Twilio,
            ServiceType = ServiceType.Sms,
            DisplayName = "Twilio",
            ConfigJson = JsonDocument.Parse("""{"test_to":"+15005550006","test_from":"+15005550001"}""")
        };
    }

    private static HttpClient CreateMockHttpClient(System.Net.HttpStatusCode statusCode, string content) =>
        new HttpClient(new MockHttpMessageHandler(statusCode, content))
        {
            BaseAddress = new Uri("https://test.example.com")
        };

    // ── Mailgun probe ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MailgunProbe_WithOutageSimulation_ReturnsOutageResult()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);
        var probe = new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_mailgunConfig);

        result.Status.Should().Be(HealthStatus.MajorOutage);
        result.SuccessRate.Should().Be(0);
        result.ErrorCode.Should().Be("SERVICE_UNAVAILABLE");
    }

    [Fact]
    public async Task MailgunProbe_WithDegradationSimulation_ReturnsDegradedResult()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunPartialDegradation);
        var probe = new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_mailgunConfig);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.SuccessRate.Should().Be(62m);
        result.LatencyMs.Should().Be(4800);
    }

    [Fact]
    public async Task MailgunProbe_NoSimulation_ReturnsOperational()
    {
        var probe = new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_mailgunConfig);

        result.Status.Should().Be(HealthStatus.Operational);
        result.SuccessRate.Should().Be(100m);
    }

    // ── SES probe ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SesProbe_WithOutageSimulation_ReturnsOutage()
    {
        _simulator.ActivateScenario(FailureScenario.SesCompleteOutage);
        var probe = new SesProbeService(_simulator, NullLogger<SesProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_sesConfig);

        result.Status.Should().Be(HealthStatus.MajorOutage);
        result.ErrorCode.Should().Be("AWS_SES_UNAVAILABLE");
    }

    [Fact]
    public async Task SesProbe_NoSimulation_ReturnsOperational()
    {
        var probe = new SesProbeService(_simulator, NullLogger<SesProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_sesConfig);
        result.Status.Should().Be(HealthStatus.Operational);
    }

    // ── Twilio probe ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TwilioProbe_WithOutageSimulation_ReturnsOutage()
    {
        _simulator.ActivateScenario(FailureScenario.TwilioCompleteOutage);
        var probe = new TwilioProbeService(_simulator, NullLogger<TwilioProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_twilioConfig);

        result.Status.Should().Be(HealthStatus.MajorOutage);
        result.ErrorCode.Should().Be("TWILIO_UNAVAILABLE");
    }

    [Fact]
    public async Task TwilioProbe_WithPartialDegradation_ReturnsDegraded()
    {
        _simulator.ActivateScenario(FailureScenario.TwilioPartialDegradation);
        var probe = new TwilioProbeService(_simulator, NullLogger<TwilioProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_twilioConfig);

        result.Status.Should().Be(HealthStatus.Degraded);
        result.SuccessRate.Should().Be(68m);
        result.ErrorCode.Should().Be("CARRIER_ISSUES");
    }

    // ── Source property ───────────────────────────────────────────────────────

    [Fact]
    public async Task InternalProbe_HasCorrectSource()
    {
        var probe = new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.RunInternalProbeAsync(_mailgunConfig);
        result.Source.Should().Be(ProbeSource.InternalSyntheticProbe);
    }

    [Fact]
    public async Task ExternalStatusCheck_HasCorrectSource()
    {
        _simulator.ActivateScenario(FailureScenario.MailgunCompleteOutage);
        var probe = new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"));

        var result = await probe.CheckExternalStatusAsync(_mailgunConfig);
        result.Source.Should().Be(ProbeSource.ExternalStatusPage);
    }

    // ── Provider identity ───────────────────────────────────────────���─────────

    [Fact]
    public void MailgunProbe_HasCorrectProvider() =>
        new MailgunProbeService(_simulator, NullLogger<MailgunProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"))
            .Provider.Should().Be(ServiceProvider.Mailgun);

    [Fact]
    public void SesProbe_HasCorrectProvider() =>
        new SesProbeService(_simulator, NullLogger<SesProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"))
            .Provider.Should().Be(ServiceProvider.Ses);

    [Fact]
    public void TwilioProbe_HasCorrectProvider() =>
        new TwilioProbeService(_simulator, NullLogger<TwilioProbeService>.Instance,
            CreateMockHttpClient(System.Net.HttpStatusCode.OK, "{}"))
            .Provider.Should().Be(ServiceProvider.Twilio);
}

/// <summary>
/// Simple mock HTTP handler for tests that need the HTTP client but shouldn't make real calls.
/// </summary>
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly System.Net.HttpStatusCode _statusCode;
    private readonly string _content;

    public MockHttpMessageHandler(System.Net.HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json")
        });
}
