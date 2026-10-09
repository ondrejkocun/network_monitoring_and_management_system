using NetworkMonitoringSystem.Application;
using NetworkMonitoringSystem.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure();

var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
