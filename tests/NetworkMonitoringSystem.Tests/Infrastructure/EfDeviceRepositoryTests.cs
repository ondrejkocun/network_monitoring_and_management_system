namespace NetworkMonitoringSystem.Tests.Infrastructure;

using System.Net;
using Microsoft.EntityFrameworkCore;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Devices;

[Trait("Category", "Integration")]
public class EfDeviceRepositoryTests : IClassFixture<DatabaseFixture>
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _database;

    public EfDeviceRepositoryTests(DatabaseFixture database)
    {
        _database = database;
    }

    [Fact]
    public async Task AddAsync_PersistsDevice_SoThatAnotherContextCanReadIt()
    {
        var device = new Device(
            Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, IPAddress.Parse("192.168.50.1"), CreatedAt);

        await using (var writeContext = _database.CreateDbContext())
        {
            await new EfDeviceRepository(writeContext).AddAsync(device);
        }

        await using var readContext = _database.CreateDbContext();
        var stored = await new EfDeviceRepository(readContext).GetByIdAsync(device.Id);

        Assert.NotNull(stored);
        Assert.Equal("Router", stored.Name);
        Assert.Equal(MonitoringMode.Agentless, stored.MonitoringMode);
        Assert.Null(stored.HostName);
        Assert.Equal(IPAddress.Parse("192.168.50.1"), stored.IpAddress);
        Assert.Equal(CreatedAt, stored.CreatedAt);
        Assert.Equal(DeviceStatus.Unknown, stored.Status);
        Assert.Null(stored.LastSeenAt);
        Assert.True(stored.IsEnabled);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenDeviceDoesNotExist()
    {
        await using var dbContext = _database.CreateDbContext();

        var stored = await new EfDeviceRepository(dbContext).GetByIdAsync(Guid.NewGuid());

        Assert.Null(stored);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsDevicesOrderedByName()
    {
        var prefix = Guid.NewGuid().ToString("N");

        await using (var writeContext = _database.CreateDbContext())
        {
            var repository = new EfDeviceRepository(writeContext);
            await repository.AddAsync(CreateAgentDevice($"{prefix}-b"));
            await repository.AddAsync(CreateAgentDevice($"{prefix}-a"));
        }

        await using var readContext = _database.CreateDbContext();
        var devices = await new EfDeviceRepository(readContext).GetAllAsync();

        var names = devices.Select(device => device.Name).Where(name => name.StartsWith(prefix)).ToList();
        Assert.Equal([$"{prefix}-a", $"{prefix}-b"], names);
    }

    [Fact]
    public async Task Migrations_SeedDefaultMonitoringSettings()
    {
        await using var dbContext = _database.CreateDbContext();

        var settings = await dbContext.MonitoringSettings.SingleAsync();

        Assert.Equal(MonitoringSettings.SingletonId, settings.Id);
        Assert.Equal(60, settings.SyncIntervalSeconds);
    }

    private static Device CreateAgentDevice(string name)
    {
        return new Device(Guid.NewGuid(), name, MonitoringMode.Agent, "server.local", null, CreatedAt);
    }
}
