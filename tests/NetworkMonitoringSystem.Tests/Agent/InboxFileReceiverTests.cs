namespace NetworkMonitoringSystem.Tests.Agent;

using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Contracts.Agents;

public class InboxFileReceiverTests
{
    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Setup 2.1 (final).msi")]
    [InlineData(".gitignore")]
    public void SafeFileName_IsAccepted(string fileName)
    {
        Assert.True(InboxFileReceiver.IsSafeFileName(fileName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" notes.txt")]
    [InlineData("..\\Windows\\System32\\evil.dll")]
    [InlineData("sub/notes.txt")]
    [InlineData("C:notes.txt")]
    [InlineData("a|b.txt")]
    [InlineData("..")]
    [InlineData("notes.")]
    [InlineData("LPT1.txt")]
    public void FileName_ThatCouldLeaveTheInbox_IsRefused(string? fileName)
    {
        Assert.False(InboxFileReceiver.IsSafeFileName(fileName));
    }

    [Fact]
    public async Task UnsafeName_IsRefused_BeforeAnythingIsDownloaded()
    {
        // The address is never contacted: the name is refused first.
        using var channel = GrpcChannel.ForAddress("https://localhost:1");
        var receiver = new InboxFileReceiver(new AgentApi.AgentApiClient(channel), Options.Create(new AgentOptions { InboxPath = Path.GetTempPath() }), NullLogger<InboxFileReceiver>.Instance);

        var result = await receiver.ReceiveAsync(
            new AgentCommand { Id = Guid.NewGuid().ToString(), Type = CommandType.SendFile, Parameter = "..\\evil.exe" },
            new AgentCredentials { DeviceId = Guid.NewGuid().ToString(), AgentKey = "key" },
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("not acceptable", result.Message);
    }

    [Fact]
    public void Inbox_IsOneFolderUnderProgramData_ByDefault()
    {
        using var channel = GrpcChannel.ForAddress("https://localhost:1");
        var receiver = new InboxFileReceiver(new AgentApi.AgentApiClient(channel), Options.Create(new AgentOptions()), NullLogger<InboxFileReceiver>.Instance);

        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NetworkMonitoringSystem", "Inbox"),
            receiver.InboxPath);
    }
}
