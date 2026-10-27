namespace NetworkMonitoringSystem.Tests.Application;

using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Monitoring;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class EnrollmentCodeTests
{
    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly InMemoryEnrollmentCodeRepository _codes = new();
    private readonly CountingUnitOfWork _unitOfWork = new();
    private readonly MutableTimeProvider _time = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly AuthenticatedUser _admin = new(Guid.NewGuid(), "admin", []);
    private readonly EnrollmentCodeService _service;

    public EnrollmentCodeTests()
    {
        _service = new EnrollmentCodeService(_codes, _devices, _history, _unitOfWork, _time);
    }

    [Fact]
    public async Task NewCode_IsShownOnce_StoredOnlyAsHash_AndItsCreationIsLogged()
    {
        var created = (await _service.CreateAsync(_admin, " Učebňa 3 – PC 1 ", null, TimeSpan.FromHours(24)))!;

        Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", created.Code);
        var stored = Assert.Single(_codes.Codes);
        Assert.DoesNotContain(EnrollmentCodeText.Normalize(created.Code), stored.CodeHash);
        Assert.Equal(("Učebňa 3 – PC 1", EnrollmentCodeState.Valid, _time.GetUtcNow().AddHours(24)), (created.Info.Note, created.Info.State, created.Info.ExpiresAt));

        var logged = Assert.Single(_history.Events);
        Assert.Equal((MonitoringEventType.EnrollmentCodeCreated, _admin.Id), (logged.Type, logged.UserId));
        Assert.DoesNotContain(created.Code, logged.Message);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8 * 24 * 60)]
    public async Task Lifetime_OutsideTenMinutesToSevenDays_IsRefused(int minutes)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(_admin, null, null, TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public async Task Code_RegistersANewComputer_Once_WhateverWayItIsTyped()
    {
        var created = (await _service.CreateAsync(_admin, null, null, TimeSpan.FromHours(1)))!;

        var registration = await Agents().RegisterAsync(new RegisterAgentRequest($"  {created.Code.Replace("-", " ").ToLowerInvariant()} ", "PC-01", null));

        Assert.Equal("PC-01", (await _devices.GetByIdAsync(registration.Credentials.DeviceId))!.HostName);
        var code = Assert.Single(_codes.Codes);
        Assert.Equal((EnrollmentCodeState.Used, "PC-01"), (code.StateAt(_time.GetUtcNow()), code.UsedByHostName));

        // Used: a second computer with the same code is refused.
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(created.Code, "PC-02", null)));
    }

    [Fact]
    public async Task ExpiredCode_IsRefused()
    {
        var created = (await _service.CreateAsync(_admin, null, null, TimeSpan.FromHours(1)))!;
        _time.Advance(TimeSpan.FromHours(1));

        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(created.Code, "PC-01", null)));
        Assert.Equal(EnrollmentCodeState.Expired, Assert.Single(await _service.GetRecentAsync()).State);
    }

    [Fact]
    public async Task RevokedCode_IsRefused_AndRevokingIsLogged()
    {
        var created = (await _service.CreateAsync(_admin, "PC v sklade", null, TimeSpan.FromHours(1)))!;

        var revoked = await _service.RevokeAsync(_admin, created.Info.Id);

        Assert.Equal(EnrollmentCodeState.Revoked, revoked!.State);
        Assert.Contains(_history.Events, monitoringEvent => monitoringEvent.Type == MonitoringEventType.EnrollmentCodeRevoked);
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(created.Code, "PC-01", null)));
        Assert.Empty(await _devices.GetAllAsync());
    }

    [Fact]
    public async Task KnownDevice_RegistersAgainOnlyWithACodeMadeForIt()
    {
        var device = new Device(Guid.NewGuid(), "PC-01", MonitoringMode.Agent, "PC-01", null, _time.GetUtcNow());
        _devices.Add(device);
        var forNewComputer = (await _service.CreateAsync(_admin, null, null, TimeSpan.FromHours(1)))!;
        var forThisDevice = (await _service.CreateAsync(_admin, null, device.Id, TimeSpan.FromHours(1)))!;

        // A code for a new computer cannot take over a device the server knows.
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(forNewComputer.Code, "PC-01", null)));
        // Nor can a code made for this device register another computer.
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(forThisDevice.Code, "PC-02", null)));

        var registration = await Agents().RegisterAsync(new RegisterAgentRequest(forThisDevice.Code, "PC-01", null));

        Assert.Equal(device.Id, registration.Credentials.DeviceId);
        Assert.Contains("registered with an enrollment code", _history.Events.Single(e => e.Type == MonitoringEventType.AgentRegistered).Message);
    }

    [Fact]
    public async Task WithoutSharedToken_AnythingButAValidCodeIsRefused()
    {
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest("enrollment-token", "PC-01", null)));
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => Agents().RegisterAsync(new RegisterAgentRequest(EnrollmentCodeText.Generate(), "PC-01", null)));
    }

    [Fact]
    public async Task CodeForAComputerWithoutAgent_CannotBeCreated()
    {
        var router = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Parse("192.0.2.1"), _time.GetUtcNow());
        _devices.Add(router);

        Assert.Null(await _service.CreateAsync(_admin, null, router.Id, TimeSpan.FromHours(1)));
        Assert.Null(await _service.CreateAsync(_admin, null, Guid.NewGuid(), TimeSpan.FromHours(1)));
    }

    private AgentService Agents()
    {
        var activity = new InMemoryDeviceActivityRepository(_history);

        return new AgentService(
            _devices,
            new FixedSettingsRepository(new MonitoringSettings()),
            _history,
            new AvailabilityRecorder(_history),
            new DeviceActivityRecorder(activity, _history),
            activity,
            _unitOfWork,
            Options.Create(new AgentEnrollmentOptions { EnrollmentToken = null }),
            _codes,
            _time);
    }
}
