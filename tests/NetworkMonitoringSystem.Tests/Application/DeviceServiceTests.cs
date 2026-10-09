namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Devices;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class DeviceServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly DeviceService _service;

    public DeviceServiceTests()
    {
        _service = new DeviceService(
            new InMemoryDeviceRepository(),
            _history,
            new InMemoryDeviceActivityRepository(_history),
            _unitOfWork,
            new MutableTimeProvider(Now));
    }

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
        Assert.Equal(1, _unitOfWork.SaveCount);
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
    public async Task RemoveDeviceAsync_RemovesDevice_AndWritesEvent()
    {
        var registered = await _service.RegisterDeviceAsync(
            new RegisterDeviceRequest("Router", MonitoringMode.Agentless, IpAddress: "192.168.50.1"));

        var removed = await _service.RemoveDeviceAsync(registered.Id);

        Assert.True(removed);
        Assert.Null(await _service.GetDeviceAsync(registered.Id));
        Assert.Equal(2, _unitOfWork.SaveCount);

        var removalEvent = Assert.Single(_history.Events);
        Assert.Equal(MonitoringEventType.DeviceRemoved, removalEvent.Type);
        Assert.Null(removalEvent.DeviceId);
        Assert.Contains("Router", removalEvent.Message);
    }

    [Fact]
    public async Task RemoveDeviceAsync_ReturnsFalse_WhenDeviceDoesNotExist()
    {
        var removed = await _service.RemoveDeviceAsync(Guid.NewGuid());

        Assert.False(removed);
        Assert.Empty(_history.Events);
        Assert.Equal(0, _unitOfWork.SaveCount);
    }

    [Fact]
    public async Task GetDevicesAsync_ReturnsAllRegisteredDevicesOrderedByName()
    {
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Switch", MonitoringMode.Agent, "switch.local"));
        await _service.RegisterDeviceAsync(new RegisterDeviceRequest("Router", MonitoringMode.Agent, "router.local"));

        var devices = await _service.GetDevicesAsync();

        Assert.Equal(["Router", "Switch"], devices.Select(device => device.Name));
    }
}
