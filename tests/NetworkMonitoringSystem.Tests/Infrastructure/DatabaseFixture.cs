namespace NetworkMonitoringSystem.Tests.Infrastructure;

using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

/// <summary>
/// Starts a throwaway PostgreSQL container and applies the migrations to it. Requires a running Docker engine.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17").Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public MonitoringDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<MonitoringDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        return new MonitoringDbContext(options);
    }
}
