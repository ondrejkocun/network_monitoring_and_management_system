namespace NetworkMonitoringSystem.Tests.Application;

using NetworkMonitoringSystem.Application.Access;
using NetworkMonitoringSystem.Application.Agents;
using NetworkMonitoringSystem.Application.Commands;
using NetworkMonitoringSystem.Domain.Access;
using NetworkMonitoringSystem.Domain.Commands;
using NetworkMonitoringSystem.Domain.Devices;
using NetworkMonitoringSystem.Domain.Monitoring;
using NetworkMonitoringSystem.Tests.Fakes;

public class RemoteCommandServiceTests
{
    private const string AgentKeyText = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryRemoteCommandRepository _commands = new();
    private readonly InMemoryFileTransferRepository _files;
    private readonly InMemoryDeviceRepository _devices = new();
    private readonly InMemoryMonitoringHistoryRepository _history = new();
    private readonly MutableTimeProvider _clock = new(Now);
    private readonly RemoteCommandService _service;
    private readonly AuthenticatedUser _actor = new(Guid.NewGuid(), "jana", []);
    private readonly Device _device;
    private readonly AgentCredentials _credentials;

    public RemoteCommandServiceTests()
    {
        _files = new InMemoryFileTransferRepository(_commands);
        _service = new RemoteCommandService(_commands, _files, _devices, _history, new CountingUnitOfWork(), _clock);
        _device = AddAgentDevice("PC-01", AgentKeyText);
        _credentials = new AgentCredentials(_device.Id, AgentKeyText);
    }

    [Theory]
    [InlineData(RemoteCommandType.RestartDevice, null, null)]
    [InlineData(RemoteCommandType.FlushDnsCache, "  ", null)]
    [InlineData(RemoteCommandType.KillProcess, " 1234 ", "1234")]
    [InlineData(RemoteCommandType.StopService, "Spooler", "Spooler")]
    [InlineData(RemoteCommandType.StartService, "My Service_1.2-x$", "My Service_1.2-x$")]
    [InlineData(RemoteCommandType.Ping, "192.168.50.1", "192.168.50.1")]
    [InlineData(RemoteCommandType.Ping, "::1", "::1")]
    [InlineData(RemoteCommandType.Traceroute, "Server-01.Example.COM", "server-01.example.com")]
    public void Parameter_ThatFitsTheCommand_IsAccepted_InItsStoredForm(RemoteCommandType type, string? parameter, string? expected)
    {
        Assert.Equal(expected, RemoteCommand.NormalizeParameter(type, parameter));
    }

    [Theory]
    [InlineData(RemoteCommandType.RestartDevice, "/f")]
    [InlineData(RemoteCommandType.KillProcess, "")]
    [InlineData(RemoteCommandType.KillProcess, "0")]
    [InlineData(RemoteCommandType.KillProcess, "-5")]
    [InlineData(RemoteCommandType.KillProcess, "12 & calc")]
    [InlineData(RemoteCommandType.StopService, "")]
    [InlineData(RemoteCommandType.StopService, "Spooler & shutdown /s")]
    [InlineData(RemoteCommandType.StopService, "a\"b")]
    [InlineData(RemoteCommandType.StopService, "..\\x")]
    [InlineData(RemoteCommandType.Ping, "")]
    [InlineData(RemoteCommandType.Ping, "8.8.8.8; rm -rf /")]
    [InlineData(RemoteCommandType.Ping, "-t 8.8.8.8")]
    [InlineData(RemoteCommandType.Ping, "host name")]
    [InlineData(RemoteCommandType.Traceroute, "example.com|calc")]
    [InlineData((RemoteCommandType)99, "")]
    public void Parameter_ThatDoesNotFitTheCommand_IsRefused(RemoteCommandType type, string parameter)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommand.NormalizeParameter(type, parameter));
    }

    [Fact]
    public void EveryCommand_HasItsOwnPermission()
    {
        var permissions = Enum.GetValues<RemoteCommandType>().Select(RemoteCommand.PermissionFor).ToList();

        Assert.Equal(permissions.Count, permissions.Distinct().Count());
        Assert.All(Enum.GetValues<RemoteCommandType>(), type => Assert.Equal(type.ToString(), RemoteCommand.PermissionFor(type).ToString()));
        Assert.DoesNotContain(Permission.ViewDevices, permissions);
    }

    [Fact]
    public async Task RequestedCommand_Waits_IsHandedToTheAgentOnce_AndStoresTheReportedResult()
    {
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.KillProcess, "1234");

        Assert.NotNull(requested);
        Assert.Equal((RemoteCommandStatus.Pending, "jana", "1234"), (requested.Status, requested.RequestedByUserName, requested.Parameter));

        var handed = Assert.Single(await _service.FetchPendingAsync(_credentials));
        Assert.Equal(new AgentCommand(requested.Id, RemoteCommandType.KillProcess, "1234"), handed);
        Assert.Empty(await _service.FetchPendingAsync(_credentials));

        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(await _service.CompleteAsync(_credentials, requested.Id, succeeded: true, "The process 'notepad' (1234) was ended."));

        var stored = Assert.Single(await _service.GetRecentAsync(_device.Id));
        Assert.Equal(RemoteCommandStatus.Succeeded, stored.Status);
        Assert.Equal(Now.AddSeconds(3), stored.CompletedAt);
        Assert.Equal("The process 'notepad' (1234) was ended.", stored.ResultMessage);

        // A result cannot be reported twice.
        Assert.False(await _service.CompleteAsync(_credentials, requested.Id, succeeded: false, "again"));
        Assert.Equal(RemoteCommandStatus.Succeeded, _commands.Commands[0].Status);
    }

    [Fact]
    public async Task Request_AndResult_AreWrittenToTheEventLog_WithTheUserAndTheDevice()
    {
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.StopService, "Spooler");
        await _service.FetchPendingAsync(_credentials);
        await _service.CompleteAsync(_credentials, requested!.Id, succeeded: false, "Access is denied.");

        Assert.Equal(2, _history.Events.Count);
        Assert.All(_history.Events, logged =>
        {
            Assert.Equal(MonitoringEventType.RemoteCommand, logged.Type);
            Assert.Equal((_device.Id, _actor.Id), (logged.DeviceId, logged.UserId));
            Assert.Contains("'StopService' (Spooler)", logged.Message);
        });
        Assert.Contains("jana", _history.Events[0].Message);
        Assert.Equal((EventSeverity.Warning, true), (_history.Events[1].Severity, _history.Events[1].Message.Contains("failed")));
    }

    [Fact]
    public async Task Command_WithInvalidParameter_IsNotQueued()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RequestAsync(_actor, _device.Id, RemoteCommandType.Ping, "8.8.8.8 & calc"));

        Assert.Empty(_commands.Commands);
        Assert.Empty(_history.Events);
    }

    [Fact]
    public async Task Command_IsRefused_ForDeviceWithoutAgent_DisabledOrOffline_AndUnknownDeviceHasNone()
    {
        var router = new Device(Guid.NewGuid(), "Router", MonitoringMode.Agentless, null, System.Net.IPAddress.Loopback, Now);
        _devices.Add(router);
        var offline = AddAgentDevice("PC-02", AgentKeyText);
        offline.MarkOffline();

        await Assert.ThrowsAsync<RemoteCommandRefusedException>(() => _service.RequestAsync(_actor, router.Id, RemoteCommandType.Ping, "127.0.0.1"));
        await Assert.ThrowsAsync<RemoteCommandRefusedException>(() => _service.RequestAsync(_actor, offline.Id, RemoteCommandType.Ping, "127.0.0.1"));
        Assert.Null(await _service.RequestAsync(_actor, Guid.NewGuid(), RemoteCommandType.Ping, "127.0.0.1"));
        Assert.Empty(_commands.Commands);
    }

    [Fact]
    public async Task Command_NotPickedUpInTime_Expires_AndIsNeverHandedToTheAgent()
    {
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.RestartDevice, null);

        _clock.Advance(RemoteCommand.Lifetime);

        Assert.Empty(await _service.FetchPendingAsync(_credentials));

        var stored = Assert.Single(await _service.GetRecentAsync(_device.Id));
        Assert.Equal((requested!.Id, RemoteCommandStatus.Expired), (stored.Id, stored.Status));
        Assert.Contains("did not pick", stored.ResultMessage);
    }

    [Fact]
    public async Task Command_WithoutReportedResult_Expires_AndALateResultIsNotAccepted()
    {
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.Ping, "127.0.0.1");
        await _service.FetchPendingAsync(_credentials);

        _clock.Advance(RemoteCommand.Lifetime);

        Assert.Equal(RemoteCommandStatus.Expired, Assert.Single(await _service.GetRecentAsync(_device.Id)).Status);
        Assert.False(await _service.CompleteAsync(_credentials, requested!.Id, succeeded: true, "late"));
    }

    [Fact]
    public async Task Agent_GetsOnlyCommandsOfItsOwnDevice_AndCannotReportOnAnotherOnes()
    {
        var other = AddAgentDevice("PC-02", "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210");
        var otherCredentials = new AgentCredentials(other.Id, "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210");
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.Ping, "127.0.0.1");

        Assert.Empty(await _service.FetchPendingAsync(otherCredentials));
        await _service.FetchPendingAsync(_credentials);

        Assert.False(await _service.CompleteAsync(otherCredentials, requested!.Id, succeeded: true, "not mine"));
        Assert.Equal(RemoteCommandStatus.Sent, _commands.Commands[0].Status);
    }

    [Fact]
    public async Task Agent_WithWrongKey_GetsNothing()
    {
        await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.Ping, "127.0.0.1");
        var wrong = new AgentCredentials(_device.Id, "wrong-key");

        await Assert.ThrowsAsync<AgentAuthenticationException>(() => _service.FetchPendingAsync(wrong));
        await Assert.ThrowsAsync<AgentAuthenticationException>(() => _service.CompleteAsync(wrong, Guid.NewGuid(), true, null));
        Assert.Equal(RemoteCommandStatus.Pending, _commands.Commands[0].Status);
    }

    [Fact]
    public async Task Device_CannotBeFloodedWithWaitingCommands()
    {
        for (var index = 0; index < RemoteCommandService.MaxUnfinishedCommands; index++)
        {
            await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.SynchronizeNow, null);
        }

        await Assert.ThrowsAsync<RemoteCommandRefusedException>(() => _service.RequestAsync(_actor, _device.Id, RemoteCommandType.SynchronizeNow, null));
    }

    [Fact]
    public async Task ResultMessage_IsShortened_AndStrippedOfControlCharacters()
    {
        var requested = await _service.RequestAsync(_actor, _device.Id, RemoteCommandType.Ping, "127.0.0.1");
        await _service.FetchPendingAsync(_credentials);

        await _service.CompleteAsync(_credentials, requested!.Id, succeeded: true, "line 1\nline 2\u0007\u001b[31m" + new string('x', 5000));

        var message = _commands.Commands[0].ResultMessage!;
        Assert.Equal(RemoteCommand.MaxResultLength, message.Length);
        Assert.StartsWith("line 1\nline 2[31m", message);
    }

    [Theory]
    [InlineData(" notes.txt ", "notes.txt")]
    [InlineData("Setup 2.1 (final).msi", "Setup 2.1 (final).msi")]
    [InlineData("config.json.bak", "config.json.bak")]
    public void FileName_ThatIsSafe_IsAccepted(string fileName, string expected)
    {
        Assert.Equal(expected, RemoteCommand.NormalizeParameter(RemoteCommandType.SendFile, fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..\\..\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("../evil.exe")]
    [InlineData("C:evil.txt")]
    [InlineData("a?b.txt")]
    [InlineData("..")]
    [InlineData("readme.")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("COM1.log")]
    public void FileName_ThatCouldLeaveTheInboxOrIsNotAllowedOnWindows_IsRefused(string fileName)
    {
        Assert.Throws<ArgumentException>(() => RemoteCommand.NormalizeParameter(RemoteCommandType.SendFile, fileName));
    }

    [Fact]
    public async Task SentFile_IsStoredInParts_WithItsHash_AndOnlyItsAgentCanReadIt_WhileCarryingItOut()
    {
        var content = Enumerable.Range(0, (FileTransfer.ChunkSize * 2) + 1000).Select(index => (byte)index).ToArray();

        var requested = await _service.SendFileAsync(_actor, _device.Id, "backup.zip", content);

        Assert.Equal((RemoteCommandType.SendFile, "backup.zip"), (requested!.Type, requested.Parameter));
        Assert.Equal(3, _files.Chunks.Count);
        var transfer = Assert.Single(_files.Transfers);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)), transfer.Sha256);
        Assert.Contains("'backup.zip' (2098152 bytes, SHA-256", Assert.Single(_history.Events).Message);

        // Not before the agent has taken the command over, and never by another device.
        Assert.Null(await _service.GetFileForAgentAsync(_credentials, requested.Id));
        var command = Assert.Single(await _service.FetchPendingAsync(_credentials));
        Assert.Equal((RemoteCommandType.SendFile, "backup.zip"), (command.Type, command.Parameter));

        var otherKey = new string('F', 64);
        var other = AddAgentDevice("PC-02", otherKey);
        Assert.Null(await _service.GetFileForAgentAsync(new AgentCredentials(other.Id, otherKey), requested.Id));

        var file = await _service.GetFileForAgentAsync(_credentials, requested.Id);
        Assert.Equal(("backup.zip", content.LongLength, 3), (file!.FileName, file.Size, file.ChunkCount));

        var downloaded = new List<byte>();

        for (var index = 0; index < file.ChunkCount; index++)
        {
            downloaded.AddRange((await _service.ReadFileChunkAsync(file.CommandId, index))!);
        }

        Assert.Equal(content, downloaded);
    }

    [Fact]
    public async Task FileContents_AreDeleted_WhenTheCommandFinishes_OrCanNoLongerBeCarriedOut()
    {
        var delivered = await _service.SendFileAsync(_actor, _device.Id, "a.txt", [1, 2, 3]);
        await _service.FetchPendingAsync(_credentials);
        var forgotten = await _service.SendFileAsync(_actor, _device.Id, "b.txt", [4, 5, 6]);

        await _service.CompleteAsync(_credentials, delivered!.Id, succeeded: true, "Saved.");

        Assert.Equal([forgotten!.Id], _files.Chunks.Select(chunk => chunk.CommandId));
        Assert.Equal(2, _files.Transfers.Count);

        _clock.Advance(RemoteCommand.Lifetime + TimeSpan.FromMinutes(1));
        Assert.Equal(1, await _service.DeleteFinishedFileContentsAsync());
        Assert.Empty(_files.Chunks);
    }

    [Fact]
    public async Task File_IsRefused_AsPlainCommand_WhenTooLarge_AndForUnsafeName()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.RequestAsync(_actor, _device.Id, RemoteCommandType.SendFile, "a.txt"));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendFileAsync(_actor, _device.Id, "big.bin", new byte[FileTransfer.MaxSize + 1]));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SendFileAsync(_actor, _device.Id, "..\\x.exe", [1]));

        Assert.Empty(_commands.Commands);
        Assert.Empty(_files.Transfers);
    }

    private Device AddAgentDevice(string name, string agentKey)
    {
        var device = new Device(Guid.NewGuid(), name, MonitoringMode.Agent, name.ToLowerInvariant(), null, Now);
        device.AssignAgentIdentity(HashOf(agentKey), "Windows 11");
        device.RecordContact(Now);
        _devices.Add(device);

        return device;
    }

    private static string HashOf(string key)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)));
    }
}
