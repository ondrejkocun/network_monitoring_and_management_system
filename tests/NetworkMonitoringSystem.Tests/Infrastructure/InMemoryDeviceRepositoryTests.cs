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
        await repository.AddAsync(new Device(id, "Server 01", "server-01.local"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.AddAsync(new Device(id, "Server 02", "server-02.local")));

        Assert.Single(await repository.GetAllAsync());
    }
}
