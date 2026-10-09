namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Infrastructure.Devices;

public class DeviceServiceTests
{
    private readonly DeviceService _service = new(new InMemoryDeviceRepository());

    [Fact]
    public async Task RegisterDeviceAsync_StoresDeviceAndReturnsDto()
    {
        var registered = await _service.RegisterDeviceAsync("  Server 01  ", "server-01.local");

        var stored = await _service.GetDeviceAsync(registered.Id);

        Assert.NotEqual(Guid.Empty, registered.Id);
        Assert.Equal("Server 01", registered.Name);
        Assert.Equal(registered, stored);
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenNameIsEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(" ", "server-01.local"));

        Assert.Empty(await _service.GetDevicesAsync());
    }

    [Fact]
    public async Task GetDeviceAsync_ReturnsNull_WhenDeviceDoesNotExist()
    {
        var device = await _service.GetDeviceAsync(Guid.NewGuid());

        Assert.Null(device);
    }

    [Fact]
    public async Task GetDevicesAsync_ReturnsAllRegisteredDevicesOrderedByName()
    {
        await _service.RegisterDeviceAsync("Switch", "switch.local");
        await _service.RegisterDeviceAsync("Router", "router.local");

        var devices = await _service.GetDevicesAsync();

        Assert.Equal(["Router", "Switch"], devices.Select(device => device.Name));
    }
}
