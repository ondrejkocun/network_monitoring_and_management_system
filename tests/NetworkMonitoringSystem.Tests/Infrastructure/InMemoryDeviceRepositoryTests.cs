namespace NetworkMonitoringSystem.Tests.Infrastructure;

using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Infrastructure.Devices;

public class InMemoryDeviceRepositoryTests
{
    [Fact]
    public async Task AddAsync_Throws_WhenDeviceWithSameIdAlreadyExists()
    {
        var repository = new InMemoryDeviceRepository();
        var id = Guid.NewGuid();
        await repository.AddAsync(CreateDevice(id, "Server 01"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(CreateDevice(id, "Server 02")));

        Assert.Single(await repository.GetAllAsync());
    }

    private static Device CreateDevice(Guid id, string name)
    {
        return new Device(id, name, MonitoringMode.Agent, "server.local", null, DateTimeOffset.UtcNow);
    }
}
