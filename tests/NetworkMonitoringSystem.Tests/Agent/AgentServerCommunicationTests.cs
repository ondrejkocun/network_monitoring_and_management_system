namespace NetworkMonitoringSystem.Tests.Agent;

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Tests.Infrastructure;

/// <summary>
/// Runs the real agent logic against the real server (hosted in memory) and a real PostgreSQL database.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AgentServerCommunicationTests : IClassFixture<DatabaseFixture>, IAsyncLifetime, IDisposable
{
    private const string Token = "test-enrollment-token";

    private readonly DatabaseFixture _database;
    private readonly WebApplicationFactory<Program> _server;
    private readonly GrpcChannel _channel;
    private readonly InMemoryIdentityStore _identityStore = new();

    public AgentServerCommunicationTests(DatabaseFixture database)
    {
        _database = database;
        _server = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // A non-development environment keeps the developer's own settings and secrets out of the test.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Database", database.ConnectionString);
            builder.UseSetting("Agents:EnrollmentToken", Token);
        });
        _channel = GrpcChannel.ForAddress(
            _server.Server.BaseAddress,
            new GrpcChannelOptions { HttpHandler = _server.Server.CreateHandler() });
    }

    /// <summary>
    /// Every test plays an agent on this machine starting from scratch. The server refuses to register a device
    /// it sees as online, so devices left by earlier tests are put offline first.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var dbContext = _database.CreateDbContext();

        foreach (var device in await dbContext.Devices.ToListAsync())
        {
            device.MarkOffline();
        }

        await dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _channel.Dispose();
        _server.Dispose();
    }

    [Fact]
    public async Task FirstReport_RegistersDevice_AndStoresIdentity()
    {
        var delay = await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);

        var identity = _identityStore.Load();
        Assert.NotNull(identity);
        Assert.Equal(TimeSpan.FromSeconds(60), delay);

        var device = await LoadDeviceAsync(identity.DeviceId);
        Assert.Equal(Environment.MachineName, device.HostName);
        Assert.Equal(MonitoringMode.Agent, device.MonitoringMode);
        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.NotNull(device.LastSeenAt);
        Assert.NotEqual(identity.AgentKey, device.AgentKeyHash);
    }

    [Fact]
    public async Task SecondReport_SendsHeartbeat_AndMovesLastSeenForward()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var afterRegistration = (await LoadDeviceAsync(identity.DeviceId)).LastSeenAt;

        var delay = await reporter.ReportOnceAsync(CancellationToken.None);

        var afterHeartbeat = (await LoadDeviceAsync(identity.DeviceId)).LastSeenAt;
        Assert.Equal(identity, _identityStore.Load());
        Assert.Equal(TimeSpan.FromSeconds(60), delay);
        Assert.True(afterHeartbeat > afterRegistration);
    }

    [Fact]
    public async Task Report_WithWrongEnrollmentToken_DoesNotRegister()
    {
        var delay = await CreateReporter("wrong-token").ReportOnceAsync(CancellationToken.None);

        Assert.Null(_identityStore.Load());
        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    [Fact]
    public async Task Report_WithRejectedIdentity_ClearsIt_AndRegistersAgainOnceDeviceIsOffline()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var original = _identityStore.Load()!;
        _identityStore.Save(original with { AgentKey = "tampered-key" });

        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.Null(_identityStore.Load());

        // While the server still sees the device as online, it cannot be registered again.
        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.Null(_identityStore.Load());

        await MarkDeviceOfflineAsync(original.DeviceId);

        await reporter.ReportOnceAsync(CancellationToken.None);
        var renewed = _identityStore.Load();
        Assert.NotNull(renewed);
        Assert.Equal(original.DeviceId, renewed.DeviceId);
        Assert.NotEqual(original.AgentKey, renewed.AgentKey);
    }

    [Fact]
    public async Task Heartbeat_WithMalformedDeviceId_IsUnauthenticated()
    {
        var client = new AgentApi.AgentApiClient(_channel);

        var exception = await Assert.ThrowsAsync<RpcException>(async () => await client.HeartbeatAsync(
            new HeartbeatRequest { Credentials = new AgentCredentials { DeviceId = "not-a-guid", AgentKey = "key" } }));

        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
    }

    private AgentReporter CreateReporter(string enrollmentToken)
    {
        return new AgentReporter(
            new AgentApi.AgentApiClient(_channel),
            _identityStore,
            Options.Create(new AgentOptions { EnrollmentToken = enrollmentToken, RetryIntervalSeconds = 7 }),
            NullLogger<AgentReporter>.Instance);
    }

    private async Task MarkDeviceOfflineAsync(string deviceId)
    {
        await using var dbContext = _database.CreateDbContext();
        var device = await dbContext.Devices.SingleAsync(device => device.Id == Guid.Parse(deviceId));

        device.MarkOffline();
        await dbContext.SaveChangesAsync();
    }

    private async Task<Device> LoadDeviceAsync(string deviceId)
    {
        await using var dbContext = _database.CreateDbContext();

        return await dbContext.Devices.AsNoTracking().SingleAsync(device => device.Id == Guid.Parse(deviceId));
    }

    private sealed class InMemoryIdentityStore : IAgentIdentityStore
    {
        private AgentIdentity? _identity;

        public AgentIdentity? Load() => _identity;

        public void Save(AgentIdentity identity) => _identity = identity;

        public void Clear() => _identity = null;
    }
}
