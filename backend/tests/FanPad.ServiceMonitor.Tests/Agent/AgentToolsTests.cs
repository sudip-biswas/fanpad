using FanPad.ServiceMonitor.Infrastructure.Agent;
using FluentAssertions;
using Xunit;

namespace FanPad.ServiceMonitor.Tests.Agent;

/// <summary>
/// Tests for AgentTools — validates tool definitions are correctly formed
/// and all required tools are registered.
/// </summary>
public class AgentToolsTests
{
    [Fact]
    public void GetAllTools_ReturnsExpectedToolCount()
    {
        var tools = AgentTools.GetAllTools();
        tools.Should().HaveCount(11, "all 11 agent tools must be registered");
    }

    [Fact]
    public void GetAllTools_AllToolsHaveUniqueNames()
    {
        var tools = AgentTools.GetAllTools();
        var names = tools.Select(t => t.Name).ToList();
        names.Should().OnlyHaveUniqueItems("tool names must be unique");
    }

    [Fact]
    public void GetAllTools_AllToolsHaveNonEmptyDescription()
    {
        var tools = AgentTools.GetAllTools();
        foreach (var tool in tools)
            tool.Description.Should().NotBeNullOrWhiteSpace($"tool '{tool.Name}' must have a description");
    }

    [Fact]
    public void GetAllTools_SubmitFailoverRecommendation_HasRequiredFields()
    {
        var tools = AgentTools.GetAllTools();
        var failoverTool = tools.First(t => t.Name == AgentTools.SubmitFailoverRec);

        failoverTool.InputSchema.Required.Should().Contain("service_type");
        failoverTool.InputSchema.Required.Should().Contain("to_provider");
        failoverTool.InputSchema.Required.Should().Contain("recommendation");
        failoverTool.InputSchema.Required.Should().Contain("work_plan");
    }

    [Fact]
    public void GetAllTools_OpenIncident_RequiresProviderAndTitle()
    {
        var tools = AgentTools.GetAllTools();
        var incidentTool = tools.First(t => t.Name == AgentTools.OpenIncident);

        incidentTool.InputSchema.Required.Should().Contain("provider");
        incidentTool.InputSchema.Required.Should().Contain("title");
        incidentTool.InputSchema.Required.Should().Contain("severity");
    }

    [Fact]
    public void ToolNames_MatchConstantValues()
    {
        var tools = AgentTools.GetAllTools();
        var toolNames = tools.Select(t => t.Name).ToHashSet();

        toolNames.Should().Contain(AgentTools.CheckExternalStatus);
        toolNames.Should().Contain(AgentTools.RunInternalProbe);
        toolNames.Should().Contain(AgentTools.GetHealthHistory);
        toolNames.Should().Contain(AgentTools.GetOpenIncidents);
        toolNames.Should().Contain(AgentTools.GetRoutingState);
        toolNames.Should().Contain(AgentTools.GetPendingCampaigns);
        toolNames.Should().Contain(AgentTools.SubmitFailoverRec);
        toolNames.Should().Contain(AgentTools.OpenIncident);
        toolNames.Should().Contain(AgentTools.ResolveIncident);
        toolNames.Should().Contain(AgentTools.HoldCampaign);
        toolNames.Should().Contain(AgentTools.ReleaseCampaign);
    }

    [Fact]
    public void GetAllTools_ProviderEnumsContainExpectedValues()
    {
        var tools = AgentTools.GetAllTools();
        var probeTool = tools.First(t => t.Name == AgentTools.RunInternalProbe);
        var providerProp = probeTool.InputSchema.Properties["provider"];

        providerProp.Enum.Should().Contain("mailgun");
        providerProp.Enum.Should().Contain("ses");
        providerProp.Enum.Should().Contain("twilio");
    }

    [Fact]
    public void GetAllTools_ServiceTypeEnumsContainExpectedValues()
    {
        var tools = AgentTools.GetAllTools();
        var failoverTool = tools.First(t => t.Name == AgentTools.SubmitFailoverRec);
        var serviceTypeProp = failoverTool.InputSchema.Properties["service_type"];

        serviceTypeProp.Enum.Should().Contain("email");
        serviceTypeProp.Enum.Should().Contain("sms");
    }
}
