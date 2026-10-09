using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application;
using NetworkMonitoringSystem.Application.Access;
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

builder.Services.Configure<AccessOptions>(
    builder.Configuration.GetSection(AccessOptions.SectionName));
builder.Services.AddSingleton<DeniedAccessLog>();

builder.Services.AddGrpc()
    .AddServiceOptions<AdminApiService>(options => options.Interceptors.Add<AdminAuthenticationInterceptor>());
builder.Services.AddHostedService<AvailabilityMonitorService>();
builder.Services.AddHostedService<AgentlessCheckService>();
builder.Services.AddHostedService<HistoryCleanupService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Keeps the local development database in sync with the migrations in the repository.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
}

using (var scope = app.Services.CreateScope())
{
    var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

    if (await authentication.EnsureInitialAdministratorAsync())
    {
        app.Logger.LogWarning(
            "The first administrator '{UserName}' was created with the configured initial password.",
            AuthenticationService.InitialAdministratorUserName);
    }
}

app.MapGrpcService<AgentApiService>();
app.MapGrpcService<AdminApiService>();
app.MapGet("/", () => "Network Monitoring & Management System server. Agents communicate with it over gRPC.");

app.Run();

/// <summary>Exposed so that integration tests can host the server in memory.</summary>
public partial class Program;
