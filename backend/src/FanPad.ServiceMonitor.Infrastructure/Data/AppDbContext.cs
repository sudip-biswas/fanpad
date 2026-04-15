using Microsoft.EntityFrameworkCore;
using FanPad.ServiceMonitor.Core.Models;
using FanPad.ServiceMonitor.Core.Enums;

namespace FanPad.ServiceMonitor.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<ServiceConfig> ServiceConfigs => Set<ServiceConfig>();
    public DbSet<HealthCheckResult> HealthCheckResults => Set<HealthCheckResult>();
    public DbSet<RoutingState> RoutingStates => Set<RoutingState>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentUpdate> IncidentUpdates => Set<IncidentUpdate>();
    public DbSet<AgentDecision> AgentDecisions => Set<AgentDecision>();
    public DbSet<FailoverEvent> FailoverEvents => Set<FailoverEvent>();
    public DbSet<FailoverApproval> FailoverApprovals => Set<FailoverApproval>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ServiceConfig
        b.Entity<ServiceConfig>(e =>
        {
            e.ToTable("service_configs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).HasConversion<string>().HasColumnName("provider");
            e.Property(x => x.ServiceType).HasConversion<string>().HasColumnName("service_type");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.IsPrimary).HasColumnName("is_primary");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            e.Property(x => x.ConfigJson).HasColumnName("config_json").HasColumnType("jsonb");
            e.Property(x => x.StatusPageUrl).HasColumnName("status_page_url");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // HealthCheckResult
        b.Entity<HealthCheckResult>(e =>
        {
            e.ToTable("health_check_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceConfigId).HasColumnName("service_config_id");
            e.Property(x => x.CheckedAt).HasColumnName("checked_at");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.LatencyMs).HasColumnName("latency_ms");
            e.Property(x => x.SuccessRate).HasColumnName("success_rate");
            e.Property(x => x.ErrorRate).HasColumnName("error_rate");
            e.Property(x => x.ErrorCode).HasColumnName("error_code");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.ExternalStatus).HasConversion<string>().HasColumnName("external_status");
            e.Property(x => x.InternalStatus).HasConversion<string>().HasColumnName("internal_status");
            e.Property(x => x.ProbeDetailJson).HasColumnName("probe_detail_json").HasColumnType("jsonb");
            e.Property(x => x.IsSimulated).HasColumnName("is_simulated");
            e.Property(x => x.SimulationScenario).HasColumnName("simulation_scenario");
            e.HasOne(x => x.ServiceConfig).WithMany(x => x.HealthCheckResults)
             .HasForeignKey(x => x.ServiceConfigId);
        });

        // RoutingState
        b.Entity<RoutingState>(e =>
        {
            e.ToTable("routing_states");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceType).HasConversion<string>().HasColumnName("service_type");
            e.Property(x => x.ActiveServiceConfigId).HasColumnName("active_service_config_id");
            e.Property(x => x.Action).HasConversion<string>().HasColumnName("action");
            e.Property(x => x.Reason).HasColumnName("reason");
            e.Property(x => x.ChangedAt).HasColumnName("changed_at");
            e.Property(x => x.ChangedBy).HasColumnName("changed_by");
            e.HasIndex(x => x.ServiceType).IsUnique();
            e.HasOne(x => x.ActiveServiceConfig).WithMany()
             .HasForeignKey(x => x.ActiveServiceConfigId);
        });

        // Incident
        b.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceConfigId).HasColumnName("service_config_id");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Severity).HasConversion<string>().HasColumnName("severity");
            e.Property(x => x.Status).HasConversion<string>().HasColumnName("status");
            e.Property(x => x.OpenedAt).HasColumnName("opened_at");
            e.Property(x => x.ResolvedAt).HasColumnName("resolved_at");
            e.Property(x => x.WorkPlan).HasColumnName("work_plan");
            e.Property(x => x.AffectedCampaigns).HasColumnName("affected_campaigns");
            e.Property(x => x.IsSimulated).HasColumnName("is_simulated");
            e.Property(x => x.SimulationScenario).HasColumnName("simulation_scenario");
            e.HasOne(x => x.ServiceConfig).WithMany(x => x.Incidents)
             .HasForeignKey(x => x.ServiceConfigId);
        });

        // IncidentUpdate
        b.Entity<IncidentUpdate>(e =>
        {
            e.ToTable("incident_updates");
            e.HasKey(x => x.Id);
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.Message).HasColumnName("message");
            e.Property(x => x.Author).HasColumnName("author");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.Incident).WithMany(x => x.Updates)
             .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        });

        // AgentDecision
        b.Entity<AgentDecision>(e =>
        {
            e.ToTable("agent_decisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TriggerType).HasColumnName("trigger_type");
            e.Property(x => x.TriggerContext).HasColumnName("trigger_context").HasColumnType("jsonb");
            e.Property(x => x.InputSummary).HasColumnName("input_summary");
            e.Property(x => x.Reasoning).HasColumnName("reasoning");
            e.Property(x => x.Decision).HasColumnName("decision");
            e.Property(x => x.DecisionDetail).HasColumnName("decision_detail");
            e.Property(x => x.ActionsTaken).HasColumnName("actions_taken").HasColumnType("jsonb");
            e.Property(x => x.WorkPlan).HasColumnName("work_plan");
            e.Property(x => x.ModelUsed).HasColumnName("model_used");
            e.Property(x => x.PromptTokens).HasColumnName("prompt_tokens");
            e.Property(x => x.CompletionTokens).HasColumnName("completion_tokens");
            e.Property(x => x.DecidedAt).HasColumnName("decided_at");
            e.Property(x => x.DurationMs).HasColumnName("duration_ms");
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.FailoverEventId).HasColumnName("failover_event_id");
        });

        // FailoverEvent
        b.Entity<FailoverEvent>(e =>
        {
            e.ToTable("failover_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceType).HasConversion<string>().HasColumnName("service_type");
            e.Property(x => x.FromProvider).HasConversion<string>().HasColumnName("from_provider");
            e.Property(x => x.ToProvider).HasConversion<string>().HasColumnName("to_provider");
            e.Property(x => x.IncidentId).HasColumnName("incident_id");
            e.Property(x => x.Authority).HasConversion<string>().HasColumnName("authority");
            e.Property(x => x.AgentRecommendation).HasColumnName("agent_recommendation");
            e.Property(x => x.WorkPlan).HasColumnName("work_plan");
            e.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            e.Property(x => x.InitiatedAt).HasColumnName("initiated_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.RevertedAt).HasColumnName("reverted_at");
            e.Property(x => x.Success).HasColumnName("success");
            e.Property(x => x.IsSimulated).HasColumnName("is_simulated");
            e.HasOne(x => x.Incident).WithMany(x => x.FailoverEvents)
             .HasForeignKey(x => x.IncidentId).IsRequired(false);
        });

        // FailoverApproval
        b.Entity<FailoverApproval>(e =>
        {
            e.ToTable("failover_approvals");
            e.HasKey(x => x.Id);
            e.Property(x => x.AgentDecisionId).HasColumnName("agent_decision_id");
            e.Property(x => x.ServiceType).HasConversion<string>().HasColumnName("service_type");
            e.Property(x => x.FromProvider).HasConversion<string>().HasColumnName("from_provider");
            e.Property(x => x.ToProvider).HasConversion<string>().HasColumnName("to_provider");
            e.Property(x => x.AgentRecommendation).HasColumnName("agent_recommendation");
            e.Property(x => x.WorkPlan).HasColumnName("work_plan");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            e.Property(x => x.ReviewNote).HasColumnName("review_note");
            e.Property(x => x.RequestedAt).HasColumnName("requested_at");
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasOne(x => x.AgentDecision).WithMany()
             .HasForeignKey(x => x.AgentDecisionId);
        });

        // Campaign
        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.ArtistName).HasColumnName("artist_name");
            e.Property(x => x.ServiceTypes)
             .HasColumnName("service_types")
             .HasColumnType("text[]")
             .HasConversion(
                v => v.Select(s => s.ToString().ToLower()).ToArray(),
                v => v.Select(s => Enum.Parse<ServiceType>(s, true)).ToArray()
             );
            e.Property(x => x.ScheduledAt).HasColumnName("scheduled_at");
            e.Property(x => x.GateStatus).HasConversion<string>().HasColumnName("gate_status");
            e.Property(x => x.GateCheckedAt).HasColumnName("gate_checked_at");
            e.Property(x => x.HoldReason).HasColumnName("hold_reason");
            e.Property(x => x.RerouteDetail).HasColumnName("reroute_detail").HasColumnType("jsonb");
            e.Property(x => x.AgentDecisionId).HasColumnName("agent_decision_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.AgentDecision).WithMany(x => x.Campaigns)
             .HasForeignKey(x => x.AgentDecisionId).IsRequired(false);
        });
    }
}
