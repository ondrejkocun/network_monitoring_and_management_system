namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Infrastructure.Devices;

public class AgentServiceTests
{
    private const string Token = "enrollment-token";

    private readonly InMemoryDeviceRepository _devices = new();
    private readonly MonitoringSettings _settings = new(syncIntervalSeconds: 120);
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task RegisterAsync_CreatesOnlineAgentDevice_AndReturnsCredentialsAndInterval()
    {
        var registration = await CreateService().RegisterAsync(new RegisterAgentRequest(Token, " PC-01 ", "Windows 11"));

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);

        Assert.NotNull(device);
        Assert.Equal("PC-01", device.HostName);
        Assert.Equal(MonitoringMode.Agent, device.MonitoringMode);
        Assert.Equal("Windows 11", device.OperatingSystem);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(_time.GetUtcNow(), device.LastSeenAt);
        Assert.Equal(120, registration.SyncIntervalSeconds);
    }

    [Fact]
    public async Task RegisterAsync_StoresOnlyHashOfTheKey()
    {
        var registration = await CreateService().RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);

        Assert.NotEmpty(registration.Credentials.AgentKey);
        Assert.NotNull(device!.AgentKeyHash);
        Assert.NotEqual(registration.Credentials.AgentKey, device.AgentKeyHash);
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("")]
    public async Task RegisterAsync_Throws_WhenTokenIsWrong(string token)
    {
        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().RegisterAsync(new RegisterAgentRequest(token, "PC-01", null)));

        Assert.Empty(await _devices.GetAllAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RegisterAsync_Throws_WhenServerHasNoTokenConfigured(string? configuredToken)
    {
        var service = CreateService(configuredToken);

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => service.RegisterAsync(new RegisterAgentRequest(configuredToken ?? string.Empty, "PC-01", null)));
    }

    [Fact]
    public async Task RegisterAsync_ReusesDeviceWithSameHostName_AndInvalidatesOldKey()
    {
        var service = CreateService();
        var first = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        var second = await service.RegisterAsync(new RegisterAgentRequest(Token, "pc-01", null));

        Assert.Equal(first.Credentials.DeviceId, second.Credentials.DeviceId);
        Assert.Single(await _devices.GetAllAsync());
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => service.ReportHeartbeatAsync(first.Credentials));
        await service.ReportHeartbeatAsync(second.Credentials);
    }

    [Fact]
    public async Task ReportHeartbeatAsync_UpdatesLastSeen_AndReturnsInterval()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));
        _time.Advance(TimeSpan.FromMinutes(5));

        var result = await service.ReportHeartbeatAsync(registration.Credentials);

        var device = await _devices.GetByIdAsync(registration.Credentials.DeviceId);
        Assert.Equal(_time.GetUtcNow(), device!.LastSeenAt);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(120, result.SyncIntervalSeconds);
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_WhenKeyIsWrong()
    {
        var service = CreateService();
        var registration = await service.RegisterAsync(new RegisterAgentRequest(Token, "PC-01", null));

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => service.ReportHeartbeatAsync(registration.Credentials with { AgentKey = "wrong-key" }));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_WhenDeviceIsUnknown()
    {
        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().ReportHeartbeatAsync(new AgentCredentials(Guid.NewGuid(), "any-key")));
    }

    [Fact]
    public async Task ReportHeartbeatAsync_Throws_ForDeviceWithoutAgent()
    {
        var device = new Device(
            Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Loopback, _time.GetUtcNow());
        await _devices.AddAsync(device);

        await Assert.ThrowsAsync<AgentAuthenticationException>(
            () => CreateService().ReportHeartbeatAsync(new AgentCredentials(device.Id, string.Empty)));
    }

    private AgentService CreateService(string? configuredToken = Token)
    {
        return new AgentService(
            _devices,
            new FixedSettingsRepository(_settings),
            Options.Create(new AgentEnrollmentOptions { EnrollmentToken = configuredToken }),
            _time);
    }

    private sealed class FixedSettingsRepository(MonitoringSettings settings) : IMonitoringSettingsRepository
    {
        public Task<MonitoringSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
