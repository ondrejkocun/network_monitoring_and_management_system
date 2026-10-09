namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Infrastructure.Devices;

public class DeviceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly DeviceService _service = new(new InMemoryDeviceRepository(), new FixedTimeProvider(Now));

    [Fact]
    public async Task RegisterDeviceAsync_StoresDeviceAndReturnsDto()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("  Server 01  ", MonitoringMode.Agent, "server-01.local"));

        var stored = await _service.GetDeviceAsync(registered.Id);

        Assert.NotEqual(Guid.Empty, registered.Id);
        Assert.Equal("Server 01", registered.Name);
        Assert.Equal(Now, registered.CreatedAt);
        Assert.Equal(DeviceStatus.Unknown, registered.Status);
        Assert.Equal(registered, stored);
    }

    [Fact]
    public async Task RegisterDeviceAsync_RegistersDeviceWithoutAgentByIpAddress()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: " 192.168.50.1 "));

        Assert.Equal(MonitoringMode.Agentless, registered.MonitoringMode);
        Assert.Equal("192.168.50.1", registered.IpAddress);
        Assert.Null(registered.HostName);
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenIpAddressIsInvalid()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "not-an-address")));

        Assert.Empty(await _service.GetDevicesAsync());
    }

    [Fact]
    public async Task RegisterDeviceAsync_Throws_WhenNameIsEmpty()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RegisterDeviceAsync(
            new RegisterDeviceRequest(" ", MonitoringMode.Agent, "server-01.local")));

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
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Switch", MonitoringMode.Agent, "switch.local"));
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Router", MonitoringMode.Agent, "router.local"));

        var devices = await _service.GetDevicesAsync();

        Assert.Equal(["Router", "Switch"], devices.Select(device => device.Name));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
