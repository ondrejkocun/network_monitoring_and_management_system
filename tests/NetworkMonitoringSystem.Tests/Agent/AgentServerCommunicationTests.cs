namespace NetworkMonitoringSystem.Tests.Agent;

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Agent.Metrics;
using NetworkMonitoringSystem.Agent.Traffic;
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
    private readonly FakeTrafficCapture _trafficCapture = new();

    /// <summary>Files the tests send arrive here instead of the real inbox; the folder is deleted afterwards.</summary>
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), "nms-inbox-" + Guid.NewGuid().ToString("N"));

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
    /// it sees as online or disabled, so devices left by earlier tests are enabled and put offline first.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var dbContext = _database.CreateDbContext();

        foreach (var device in await dbContext.Devices.ToListAsync())
        {
            device.Enable();
            device.SetTrafficCapture(false);
            device.MarkOffline();
        }

        await dbContext.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _channel.Dispose();
        _server.Dispose();

        if (Directory.Exists(_inbox))
        {
            Directory.Delete(_inbox, recursive: true);
        }
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
    public async Task Report_StoresTheAddressOfThisComputerInTheNetwork()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        await reporter.ReportOnceAsync(CancellationToken.None);

        var device = await LoadDeviceAsync(_identityStore.Load()!.DeviceId);
        var expected = NetworkAddress.FindPrimary();

        Assert.False(string.IsNullOrEmpty(expected), "This computer has no active network adapter with an IPv4 address.");
        Assert.Equal(expected, device.IpAddress?.ToString());
        Assert.False(System.Net.IPAddress.IsLoopback(device.IpAddress!));
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
    public async Task CaptureSwitchedOnForDevice_StartsCaptureInAgent_AndItsSummaryIsStored_UntilSwitchedOff()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var deviceId = Guid.Parse(_identityStore.Load()!.DeviceId);

        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.False(_trafficCapture.IsEnabled);

        await SetTrafficCaptureAsync(deviceId, true);
        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.True(_trafficCapture.IsEnabled);

        await reporter.ReportOnceAsync(CancellationToken.None);

        await using (var dbContext = _database.CreateDbContext())
        {
            var record = await dbContext.TrafficRecords.SingleAsync(record => record.DeviceId == deviceId);
            Assert.Equal(("DNS", "192.0.2.53", 53, 850L), (record.Protocol, record.RemoteAddress, record.ServicePort, record.BytesSent + record.BytesReceived));
        }

        await SetTrafficCaptureAsync(deviceId, false);
        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.False(_trafficCapture.IsEnabled);
    }

    [Fact]
    public async Task File_SentByAdministrator_IsDownloadedByTheAgentUnchanged_AndOnlyItsRecordStaysAfterward()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var admin = new NetworkMonitoringSystem.Desktop.Services.GrpcServerClient(new AdminContracts.AdminApi.AdminApiClient(_channel));
        await admin.LoginAsync(Server.TestAdministrator.UserName, Server.TestAdministrator.Password);
        var content = new byte[(1024 * 1024 * 3) / 2];
        Random.Shared.NextBytes(content);

        var sent = await admin.SendFileAsync(identity.DeviceId, "setup.bin", new MemoryStream(content));
        Assert.Equal(("SendFile", "setup.bin", "Pending"), (sent.Type, sent.Parameter, sent.Status));

        var agent = new AgentApi.AgentApiClient(_channel);
        var credentials = new AgentCredentials { DeviceId = identity.DeviceId, AgentKey = identity.AgentKey };
        var handed = (await agent.FetchCommandsAsync(new FetchCommandsRequest { Credentials = credentials })).Commands;
        Assert.Equal(CommandType.SendFile, Assert.Single(handed, command => command.Id == sent.Id).Type);

        FileHeader? header = null;
        using var downloaded = new MemoryStream();

        using (var download = agent.DownloadFile(new DownloadFileRequest { Credentials = credentials, CommandId = sent.Id }))
        {
            while (await download.ResponseStream.MoveNext(CancellationToken.None))
            {
                if (download.ResponseStream.Current.PartCase == DownloadFileReply.PartOneofCase.Header)
                {
                    header = download.ResponseStream.Current.Header;
                }
                else
                {
                    download.ResponseStream.Current.Data.WriteTo(downloaded);
                }
            }
        }

        Assert.Equal(("setup.bin", content.LongLength), (header!.FileName, header.Size));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)), header.Sha256);
        Assert.Equal(content, downloaded.ToArray());

        await agent.ReportCommandResultAsync(new CommandResultRequest { Credentials = credentials, CommandId = sent.Id, Succeeded = true, Message = "Saved." });

        await using var dbContext = _database.CreateDbContext();
        var commandId = Guid.Parse(sent.Id);
        Assert.True(await dbContext.FileTransfers.AnyAsync(transfer => transfer.CommandId == commandId && transfer.FileName == "setup.bin"));
        Assert.False(await dbContext.FileTransferChunks.AnyAsync(chunk => chunk.CommandId == commandId));

        // Once finished, the file cannot be downloaded again.
        var again = agent.DownloadFile(new DownloadFileRequest { Credentials = credentials, CommandId = sent.Id });
        var exception = await Assert.ThrowsAsync<RpcException>(() => again.ResponseStream.MoveNext(CancellationToken.None));
        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    [Fact]
    public async Task Files_SentByAdministrator_AreSavedIntoTheInbox_WithoutOverwriting_AndTheResultSaysWhere()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var identity = _identityStore.Load()!;
        var admin = new NetworkMonitoringSystem.Desktop.Services.GrpcServerClient(new AdminContracts.AdminApi.AdminApiClient(_channel));
        await admin.LoginAsync(Server.TestAdministrator.UserName, Server.TestAdministrator.Password);
        var worker = CreateCommandWorker(new SynchronizationSignal());

        // The tests of this class share one device: commands other tests left waiting are carried out first.
        await worker.PollOnceAsync(CancellationToken.None);

        var first = new byte[300_000];
        var second = new byte[1024 * 1024 + 7];
        Random.Shared.NextBytes(first);
        Random.Shared.NextBytes(second);
        var sentFirst = await admin.SendFileAsync(identity.DeviceId, "config.json", new MemoryStream(first));
        await admin.SendFileAsync(identity.DeviceId, "config.json", new MemoryStream(second));

        await worker.PollOnceAsync(CancellationToken.None);

        Assert.Equal(first, await File.ReadAllBytesAsync(Path.Combine(_inbox, "config.json")));
        Assert.Equal(second, await File.ReadAllBytesAsync(Path.Combine(_inbox, "config (2).json")));
        Assert.Equal(2, Directory.GetFiles(_inbox).Length);

        var delivered = Assert.Single(await admin.GetDeviceCommandsAsync(identity.DeviceId), command => command.Id == sentFirst.Id);
        Assert.Equal("Succeeded", delivered.Status);
        Assert.Contains(Path.Combine(_inbox, "config.json"), delivered.ResultMessage);
        Assert.Contains(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(first)), delivered.ResultMessage);
    }

    [Fact]
    public async Task Report_WithWrongEnrollmentToken_DoesNotRegister()
    {
        var delay = await CreateReporter("wrong-token").ReportOnceAsync(CancellationToken.None);

        Assert.Null(_identityStore.Load());
        Assert.Equal(TimeSpan.FromSeconds(7), delay);
    }

    [Fact]
    public async Task EnrollmentCode_MadeByAdministrator_RegistersTheDeviceAgainOnce_AndShowsWhoUsedIt()
    {
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
        var original = _identityStore.Load()!;
        _identityStore.Clear();
        await MarkDeviceOfflineAsync(original.DeviceId);
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));

        var created = await admin.CreateEnrollmentCodeAsync(new AdminContracts.CreateEnrollmentCodeRequest
        {
            Note = "Reinstalled",
            DeviceId = original.DeviceId,
            ValidMinutes = 60,
        });
        var reporter = CreateReporter(created.Code);
        await reporter.ReportOnceAsync(CancellationToken.None);

        Assert.Equal(original.DeviceId, _identityStore.Load()!.DeviceId);
        var listed = (await admin.ListEnrollmentCodesAsync(new AdminContracts.ListEnrollmentCodesRequest())).Codes.Single(code => code.Id == created.Info.Id);
        Assert.Equal(("Used", Environment.MachineName), (listed.State, listed.UsedByHostName));

        // Spent: the same code does not register the device a second time.
        _identityStore.Clear();
        await MarkDeviceOfflineAsync(original.DeviceId);
        await reporter.ReportOnceAsync(CancellationToken.None);
        Assert.Null(_identityStore.Load());
        await CreateReporter(Token).ReportOnceAsync(CancellationToken.None);
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
    public async Task DisabledDevice_StaysDisabled_WhileItsAgentKeepsReporting_AndTheAgentReturnsOnceEnabled()
    {
        var reporter = CreateReporter(Token);
        await reporter.ReportOnceAsync(CancellationToken.None);
        var original = _identityStore.Load()!;
        var admin = new AdminContracts.AdminApi.AdminApiClient(await Server.TestAdministrator.SignInAsync(_channel));

        await admin.SetDeviceEnabledAsync(new AdminContracts.SetDeviceEnabledRequest { Id = original.DeviceId, IsEnabled = false });

        // The heartbeat is refused, the agent drops its identity and its registration is refused too.
        Assert.Equal(TimeSpan.FromSeconds(7), await reporter.ReportOnceAsync(CancellationToken.None));
        Assert.Null(_identityStore.Load());
        Assert.Equal(TimeSpan.FromSeconds(7), await reporter.ReportOnceAsync(CancellationToken.None));
        Assert.Null(_identityStore.Load());

        var disabled = await LoadDeviceAsync(original.DeviceId);
        Assert.False(disabled.IsEnabled);
        Assert.Equal(DeviceStatus.Unknown, disabled.Status);
        await using (var dbContext = _database.CreateDbContext())
        {
            Assert.Single(await dbContext.Devices.Where(device => device.HostName == Environment.MachineName).ToListAsync());
        }

        await admin.SetDeviceEnabledAsync(new AdminContracts.SetDeviceEnabledRequest { Id = original.DeviceId, IsEnabled = true });

        await reporter.ReportOnceAsync(CancellationToken.None);
        var renewed = _identityStore.Load();
        Assert.NotNull(renewed);
        Assert.Equal(original.DeviceId, renewed.DeviceId);
        Assert.Equal(DeviceStatus.Online, (await LoadDeviceAsync(original.DeviceId)).Status);
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

    [Fact]
    public async Task RegisterCommand_RegistersTheComputerOnce_AndFailsWithAWrongToken()
    {
        // What the installer runs before it creates the service.
        Assert.Equal(1, await AgentCommandLine.RegisterAsync(CreateAgentServices("wrong-token")));
        Assert.Null(_identityStore.Load());

        Assert.Equal(0, await AgentCommandLine.RegisterAsync(CreateAgentServices(Token)));
        var identity = _identityStore.Load();
        Assert.NotNull(identity);

        // Running the installer again keeps the identity the computer already has.
        Assert.Equal(0, await AgentCommandLine.RegisterAsync(CreateAgentServices(Token)));
        Assert.Equal(identity, _identityStore.Load());
    }

    private ServiceProvider CreateAgentServices(string enrollmentToken)
    {
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(Options.Create(new AgentOptions { ServerUrl = "https://localhost", EnrollmentToken = enrollmentToken }))
            .AddSingleton<IAgentIdentityStore>(_identityStore)
            .AddSingleton(CreateReporter(enrollmentToken))
            .BuildServiceProvider();
    }

    private CommandWorker CreateCommandWorker(SynchronizationSignal signal)
    {
        return new CommandWorker(
            new AgentApi.AgentApiClient(_channel),
            _identityStore,
            new WindowsCommandExecutor(new SystemProgramRunner(), signal),
            new InboxFileReceiver(
                new AgentApi.AgentApiClient(_channel),
                Options.Create(new AgentOptions { InboxPath = _inbox }),
                NullLogger<InboxFileReceiver>.Instance),
            NullLogger<CommandWorker>.Instance);
    }

    private AgentReporter CreateReporter(string enrollmentToken)
    {
        return new AgentReporter(
            new AgentApi.AgentApiClient(_channel),
            _identityStore,
            new WindowsSystemMetricsCollector(),
            new WindowsSystemActivityCollector(),
            _trafficCapture,
            Options.Create(new AgentOptions { EnrollmentToken = enrollmentToken, RetryIntervalSeconds = 7 }),
            NullLogger<AgentReporter>.Instance);
    }

    private async Task SetTrafficCaptureAsync(Guid deviceId, bool capture)
    {
        await using var dbContext = _database.CreateDbContext();
        var device = await dbContext.Devices.SingleAsync(device => device.Id == deviceId);

        device.SetTrafficCapture(capture);
        await dbContext.SaveChangesAsync();
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

    /// <summary>Stands in for TShark: remembers whether the server asked for capture and reports one DNS conversation.</summary>
    private sealed class FakeTrafficCapture : ITrafficCapture
    {
        public bool IsEnabled { get; private set; }

        public void SetEnabled(bool enabled) => IsEnabled = enabled;

        public TrafficSummary TakeSummary()
        {
            var now = DateTimeOffset.UtcNow;
            var summary = new TrafficSummary
            {
                PeriodStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now.AddSeconds(-60)),
                PeriodEnd = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(now),
            };
            summary.Conversations.Add(new TrafficConversation
            {
                Transport = NetworkMonitoringSystem.Contracts.Agents.TrafficTransport.Udp,
                Protocol = "DNS",
                RemoteAddress = "192.0.2.53",
                ServicePort = 53,
                PacketsSent = 3,
                PacketsReceived = 3,
                BytesSent = 210,
                BytesReceived = 640,
            });

            return summary;
        }

        public void Dispose()
        {
        }
    }

    private sealed class InMemoryIdentityStore : IAgentIdentityStore
    {
        private AgentIdentity? _identity;

        public AgentIdentity? Load() => _identity;

        public void Save(AgentIdentity identity) => _identity = identity;

        public void Clear() => _identity = null;
    }
}
