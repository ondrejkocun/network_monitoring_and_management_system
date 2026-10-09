using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Application;
using NetworkMonitoringSystem.Infrastructure;
using NetworkMonitoringSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Keeps the local development database in sync with the migrations in the repository.
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<MonitoringDbContext>().Database.MigrateAsync();
}

app.MapGet("/", () => "Hello World!");

app.Run();
