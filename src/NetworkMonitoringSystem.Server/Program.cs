using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Infrastructure;
using NetworkMonitoringSystem.Infrastructure.Persistence;
using NetworkMonitoringSystem.Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.Configure<AgentEnrollmentOptions>(
    builder.Configuration.GetSection(AgentEnrollmentOptions.SectionName));

builder.Services.AddGrpc();
builder.Services.AddHostedService<AvailabilityMonitorService>();
builder.Services.AddHostedService<AgentlessCheckService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Keeps the local development database in sync with the migrations in the repository.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
}

app.MapGrpcService<AgentApiService>();
app.MapGet("/", () => "Network Monitoring & Management System server. Agents communicate with it over gRPC.");

app.Run();

/// <summary>Exposed so that integration tests can host the server in memory.</summary>
public partial class Program;
