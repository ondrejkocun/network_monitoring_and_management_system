namespace NetworkMonitoringSystem.Tests.Agent;

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Agent.Metrics;
using AdminContracts = NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Contracts.Agents;
using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
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
            Server.TestAdministrator.Configure(builder);
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
    public async Task Heartbeat_StoresMeasuredResources_AndAdministratorCanReadThem()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));
        var deviceId = Guid.Parse(identity.DeviceId);

        // Registration alone carries no measurement; only a heartbeat adds one.
        var measuredBefore = await CountMeasuredSnapshotsAsync(deviceId);
        var heartbeatSentAt = DateTimeOffset.UtcNow.AddSeconds(-1);

        await reporter.ReportOnceAsync(CancellationToken.None);

        var resources = await admin.GetDeviceResourcesAsync(new AdminContracts.GetDeviceResourcesRequest { Id = identity.DeviceId });

        Assert.True(resources.HasData);
        Assert.True(resources.RecordedAt.ToDateTimeOffset() >= heartbeatSentAt);
        Assert.True(resources.HasCpuUsagePercent);
        Assert.InRange(resources.CpuUsagePercent, 0, 100);
        Assert.True(resources.MemoryTotalBytes > 0);
        Assert.InRange(resources.MemoryUsedBytes, 1UL, resources.MemoryTotalBytes);
        Assert.NotEmpty(resources.Disks);
        Assert.All(resources.Disks, disk => Assert.True(disk.FreeBytes <= disk.TotalBytes));
        Assert.NotEmpty(resources.NetworkInterfaces);

        Assert.Equal(measuredBefore + 1, await CountMeasuredSnapshotsAsync(deviceId));
    }

    private async Task<int> CountMeasuredSnapshotsAsync(Guid deviceId)
    {
        await using var dbContext = _database.CreateDbContext();

        return await dbContext.DeviceSnapshots.CountAsync(snapshot => snapshot.DeviceId == deviceId && snapshot.HasResources);
    }

    [Fact]
    public async Task Heartbeat_StoresProcessesPortsAndConnections_AndAdministratorCanReadThem()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));

        await reporter.ReportOnceAsync(CancellationToken.None);

        var activity = await admin.GetDeviceActivityAsync(new AdminContracts.GetDeviceActivityRequest { Id = identity.DeviceId });

        // The process running this test is one of the processes on this computer.
        var thisProcess = Assert.Single(activity.Processes, process => process.Pid == (uint)Environment.ProcessId);
        Assert.False(string.IsNullOrWhiteSpace(thisProcess.Name));
        Assert.NotNull(thisProcess.StartedAt);
        Assert.True(activity.Processes.Count > 10);
        Assert.Contains(activity.Processes, process => process.HasUsage);

        // Every Windows computer has listening ports, for example 135 of the RPC endpoint mapper.
        Assert.NotEmpty(activity.ListeningPorts);
        Assert.All(activity.ListeningPorts, port => Assert.InRange(port.Port, 0U, 65535U));
        Assert.Contains(activity.ListeningPorts, port => port.ProcessName.Length > 0);

        // A second identical report changes nothing: no process run or port period is duplicated.
        await reporter.ReportOnceAsync(CancellationToken.None);
        var again = await admin.GetDeviceActivityAsync(new AdminContracts.GetDeviceActivityRequest { Id = identity.DeviceId });
        Assert.Single(again.Processes, process => process.Pid == (uint)Environment.ProcessId);
        Assert.Equal(thisProcess.StartedAt, again.Processes.Single(process => process.Pid == (uint)Environment.ProcessId).StartedAt);
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

    [Fact]
    public async Task Command_SentByAdministrator_IsCarriedOutByTheAgent_AndItsResultCanBeRead()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));
        var signal = new SynchronizationSignal();
        var worker = CreateCommandWorker(signal);

        // The tests of this class share one device: a command another test left waiting is carried out first,
        // and what was logged before does not count.
        await worker.PollOnceAsync(CancellationToken.None);
        await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);
        var loggedBefore = await CountCommandEventsAsync(identity.DeviceId);

        var ping = await admin.SendCommandAsync(new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = "Ping", Parameter = "127.0.0.1" });
        var synchronize = await admin.SendCommandAsync(new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = "SynchronizeNow" });
        Assert.Equal(("Pending", "admin"), (ping.Status, ping.RequestedBy));

        Assert.Equal(TimeSpan.FromSeconds(5), await worker.PollOnceAsync(CancellationToken.None));

        var commands = (await admin.ListDeviceCommandsAsync(new AdminContracts.ListDeviceCommandsRequest { DeviceId = identity.DeviceId })).Commands;
        var finishedPing = Assert.Single(commands, command => command.Id == ping.Id);
        Assert.Equal("Succeeded", finishedPing.Status);
        Assert.Contains("replies received", finishedPing.ResultMessage);
        Assert.NotNull(finishedPing.CompletedAt);
        Assert.Equal("Succeeded", Assert.Single(commands, command => command.Id == synchronize.Id).Status);

        // The command to synchronize woke the reporting loop.
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None));

        // Nothing is carried out twice.
        await worker.PollOnceAsync(CancellationToken.None);
        Assert.False(await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None));

        // Each of the two commands is logged when it is sent and when it ends.
        Assert.Equal(loggedBefore + 4, await CountCommandEventsAsync(identity.DeviceId));
    }

    private async Task<int> CountCommandEventsAsync(string deviceId)
    {
        await using var dbContext = _database.CreateDbContext();

        return await dbContext.Events.CountAsync(logged =>
            logged.Type == MonitoringEventType.RemoteCommand && logged.DeviceId == Guid.Parse(deviceId));
    }

    [Fact]
    public async Task Command_IsRefused_WithoutItsPermission_ForInvalidInput_AndForDeviceThatIsOffline()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var deviceId = Guid.Parse(identity.DeviceId);
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));

        // A user who may look at the device and ping from it, but nothing else.
        var userName = "user-" + Guid.NewGuid().ToString("N");
        Guid userId;

        await using (var dbContext = _database.CreateDbContext())
        {
            var role = new Role(Guid.NewGuid(), "Role-" + Guid.NewGuid().ToString("N"));
            role.Allow(Permission.ViewDevices, deviceId);
            role.Allow(Permission.Ping, deviceId);

            var user = new User(
                Guid.NewGuid(),
                userName,
                PasswordHasher.Hash("operator-password"),
                DateTimeOffset.UtcNow);
            user.AssignRole(role);
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();
            userId = user.Id;
        }

        var session = await new AdminContracts.AdminApi.AdminApiClient(_channel).LoginAsync(
            new AdminContracts.LoginRequest { UserName = userName, Password = "operator-password" });
        var operatorClient = new AdminContracts.AdminApi.AdminApiClient(Server.TestAdministrator.WithToken(_channel, session.Token));

        var allowed = await operatorClient.SendCommandAsync(new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = "Ping", Parameter = "127.0.0.1" });
        Assert.Equal("Pending", allowed.Status);

        var denied = await Assert.ThrowsAsync<RpcException>(async () => await operatorClient.SendCommandAsync(
            new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = "RestartDevice" }));
        Assert.Equal(StatusCode.PermissionDenied, denied.StatusCode);

        foreach (var (type, parameter) in new[] { ("Ping", "8.8.8.8 & calc"), ("KillProcess", "abc"), ("RunProgram", "calc.exe"), ("RestartDevice", "/f") })
        {
            var invalid = await Assert.ThrowsAsync<RpcException>(async () => await admin.SendCommandAsync(
                new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = type, Parameter = parameter }));
            Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);
        }

        await MarkDeviceOfflineAsync(identity.DeviceId);

        var offline = await Assert.ThrowsAsync<RpcException>(async () => await admin.SendCommandAsync(
            new AdminContracts.SendCommandRequest { DeviceId = identity.DeviceId, Type = "Ping", Parameter = "127.0.0.1" }));
        Assert.Equal(StatusCode.FailedPrecondition, offline.StatusCode);

        await using var readContext = _database.CreateDbContext();

        // Only the allowed command was queued; the refused restart is in the event log instead.
        var queued = await readContext.RemoteCommands.SingleAsync(command => command.RequestedByUserName == userName);
        Assert.Equal((allowed.Id, deviceId), (queued.Id.ToString(), queued.DeviceId));
        Assert.True(await readContext.Events.AnyAsync(logged =>
            logged.Type == MonitoringEventType.AccessDenied
            && logged.UserId == userId
            && logged.DeviceId == deviceId
            && logged.Message.Contains("SendCommand")));
    }

    [Fact]
    public async Task Agent_WithWrongKey_GetsNoCommands_AndCannotReportResults()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var agent = new AgentApi.AgentApiClient(_channel);
        var wrong = new AgentCredentials { DeviceId = identity.DeviceId, AgentKey = "wrong-key" };

        var fetch = await Assert.ThrowsAsync<RpcException>(async () => await agent.FetchCommandsAsync(new FetchCommandsRequest { Credentials = wrong }));
        Assert.Equal(StatusCode.Unauthenticated, fetch.StatusCode);

        var report = await Assert.ThrowsAsync<RpcException>(async () => await agent.ReportCommandResultAsync(
            new CommandResultRequest { Credentials = wrong, CommandId = Guid.NewGuid().ToString(), Succeeded = true }));
        Assert.Equal(StatusCode.Unauthenticated, report.StatusCode);

        // With the right key, a result for a command the device was never handed is not accepted either.
        var unknown = await Assert.ThrowsAsync<RpcException>(async () => await agent.ReportCommandResultAsync(new CommandResultRequest
        {
            Credentials = new AgentCredentials { DeviceId = identity.DeviceId, AgentKey = identity.AgentKey },
            CommandId = Guid.NewGuid().ToString(),
            Succeeded = true,
        }));
        Assert.Equal(StatusCode.NotFound, unknown.StatusCode);
    }

    private CommandWorker CreateCommandWorker(SynchronizationSignal signal)
    {
        return new CommandWorker(
            new AgentApi.AgentApiClient(_channel),
            _identityStore,
            new WindowsCommandExecutor(new SystemProgramRunner(), signal),
            NullLogger<CommandWorker>.Instance);
    }

    private AgentReporter CreateReporter(string enrollmentToken)
    {
        return new AgentReporter(
            new AgentApi.AgentApiClient(_channel),
            _identityStore,
            new WindowsSystemMetricsCollector(),
            new WindowsSystemActivityCollector(),
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
