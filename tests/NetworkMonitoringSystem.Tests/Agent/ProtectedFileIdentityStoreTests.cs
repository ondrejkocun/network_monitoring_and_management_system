namespace NetworkMonitoringSystem.Tests.Agent;

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Identity;

public sealed class ProtectedFileIdentityStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "nms-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _filePath;

    public ProtectedFileIdentityStoreTests()
    {
        _filePath = Path.Combine(_directory, "nested", "identity.bin");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsNull_WhenNothingWasSaved()
    {
        Assert.Null(CreateStore().Load());
    }

    [Fact]
    public void Save_ThenLoad_ReturnsSameIdentity_FromNewStoreInstance()
    {
        var identity = new AgentIdentity(Guid.NewGuid().ToString(), "secret-agent-key");

        CreateStore().Save(identity);

        Assert.Equal(identity, CreateStore().Load());
    }

    [Fact]
    public void Save_DoesNotWriteTheKeyInPlainText()
    {
        CreateStore().Save(new AgentIdentity(Guid.NewGuid().ToString(), "secret-agent-key"));

        var content = Encoding.UTF8.GetString(File.ReadAllBytes(_filePath));

        Assert.DoesNotContain("secret-agent-key", content);
    }

    [Fact]
    public void Clear_RemovesStoredIdentity()
    {
        var store = CreateStore();
        store.Save(new AgentIdentity(Guid.NewGuid().ToString(), "secret-agent-key"));

        store.Clear();

        Assert.Null(store.Load());
    }

    [Fact]
    public void Load_ReturnsNull_WhenFileIsCorrupted()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, "this is not a protected identity");

        Assert.Null(CreateStore().Load());
    }

    private ProtectedFileIdentityStore CreateStore()
    {
        return new ProtectedFileIdentityStore(
            Options.Create(new AgentOptions { IdentityFilePath = _filePath }),
            NullLogger<ProtectedFileIdentityStore>.Instance);
    }
}
