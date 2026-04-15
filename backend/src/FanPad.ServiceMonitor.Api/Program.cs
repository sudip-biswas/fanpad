using Anthropic.SDK;
using FanPad.ServiceMonitor.Api.BackgroundServices;
using FanPad.ServiceMonitor.Api.Hubs;
using FanPad.ServiceMonitor.Core.Interfaces;
using FanPad.ServiceMonitor.Infrastructure.Agent;
using FanPad.ServiceMonitor.Infrastructure.Data;
using FanPad.ServiceMonitor.Infrastructure.Probes;
using FanPad.ServiceMonitor.Infrastructure.Routing;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ─── Configuration ────────────────────────────────────────────────────────────

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FanPad Service Health Monitor API", Version = "v1" });
});

// ─── Database ─────────────────────────────────────────────────────────────────

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        npgsql => npgsql.EnableRetryOnFailure()
    ));

// ─── SignalR ──────────────────────────────────────────────────────────────────

builder.Services.AddSignalR();

// ─── CORS (Angular dev server) ────────────────────────────────────────────────

builder.Services.AddCors(opts =>
    opts.AddPolicy("Angular", policy =>
        policy
            .WithOrigins("http://localhost:4200")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

// ─── Anthropic SDK ────────────────────────────────────────────────────────────

builder.Services.AddSingleton(new AnthropicClient(
    builder.Configuration["Anthropic:ApiKey"]
    ?? throw new InvalidOperationException("Anthropic:ApiKey is required")));

// ─── Health Probes ────────────────────────────────────────────────────────────

builder.Services.AddHttpClient<MailgunProbeService>();
builder.Services.AddHttpClient<SesProbeService>();
builder.Services.AddHttpClient<TwilioProbeService>();

// Register concrete probes
builder.Services.AddScoped<MailgunProbeService>();
builder.Services.AddScoped<SesProbeService>();
builder.Services.AddScoped<TwilioProbeService>();

// Register as the interface (IEnumerable<IHealthProbeService>)
builder.Services.AddScoped<IHealthProbeService>(sp => sp.GetRequiredService<MailgunProbeService>());
builder.Services.AddScoped<IHealthProbeService>(sp => sp.GetRequiredService<SesProbeService>());
builder.Services.AddScoped<IHealthProbeService>(sp => sp.GetRequiredService<TwilioProbeService>());

// ─── Core Services ────────────────────────────────────────────────────────────

// Singleton: failure simulator state persists across requests
builder.Services.AddSingleton<IFailureSimulator, FailureSimulatorService>();

builder.Services.AddScoped<IRoutingService, RoutingService>();
builder.Services.AddScoped<IAgentOrchestrationService, AgentOrchestrationService>();

// ─── Background Services ──────────────────────────────────────────────────────

builder.Services.AddHostedService<HealthMonitorBackgroundService>();

// ─── Build App ────────────────────────────────────────────────────────────────

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Angular");
app.UseRouting();
app.MapControllers();
app.MapHub<ServiceStatusHub>("/hubs/status");

// Apply pending EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
