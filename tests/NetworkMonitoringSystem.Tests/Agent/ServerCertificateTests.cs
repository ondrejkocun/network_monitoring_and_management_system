namespace NetworkMonitoringSystem.Tests.Agent;

using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Security;

/// <summary>
/// Verifies how the agent and the desktop application decide whether to trust the server, against a real
/// HTTPS server with a self-signed certificate, and where a service keeps the agent's identity.
/// </summary>
public sealed class ServerCertificateTests : IDisposable
{
    private readonly X509Certificate2 _serverCertificate = CreateSelfSignedCertificate("nms-test-server");
    private readonly X509Certificate2 _otherCertificate = CreateSelfSignedCertificate("nms-test-server");

    public void Dispose()
    {
        _serverCertificate.Dispose();
        _otherCertificate.Dispose();
    }

    [Fact]
    public void Pin_IsTheSha256OfTheCertificate_WrittenInAnyUsualForm()
    {
        var pin = CertificatePin.Of(_serverCertificate);

        Assert.Equal(64, pin.Length);
        Assert.Equal(pin, _serverCertificate.GetCertHashString(HashAlgorithmName.SHA256));
        Assert.Equal(pin, CertificatePin.Normalize(pin.ToLowerInvariant()));
        Assert.Equal(pin, CertificatePin.Normalize(string.Join(':', pin.Chunk(2).Select(pair => new string(pair)))));
        Assert.Equal(pin, CertificatePin.Normalize($"  {pin[..32]} {pin[32..]}  "));

        Assert.True(CertificatePin.Matches(_serverCertificate, pin));
        Assert.False(CertificatePin.Matches(_otherCertificate, pin));
        Assert.False(CertificatePin.Matches(null, pin));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a pin")]
    [InlineData("3F2A")]
    // A SHA-1 thumbprint, which is what the certificate store shows, is not the pin.
    [InlineData("0123456789ABCDEF0123456789ABCDEF01234567")]
    [InlineData("ZZ23456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF")]
    public void Text_ThatIsNotASha256Hash_IsNotAPin(string text)
    {
        Assert.Throws<FormatException>(() => CertificatePin.Normalize(text));
        Assert.Equal(string.IsNullOrWhiteSpace(text), AgentCommandLine.IsValidPin(text));
    }

    [Fact]
    public async Task Client_TalksOnlyToTheServerWithThePinnedCertificate()
    {
        await using var server = await StartHttpsServerAsync(_serverCertificate);
        var address = GetAddress(server);

        // The certificate is self-signed and issued for another name, yet the pinned client accepts it.
        using (var pinned = new HttpClient(CertificatePin.CreateHandler(CertificatePin.Of(_serverCertificate))))
        {
            Assert.Equal("ok", await pinned.GetStringAsync(address));
        }

        // A client pinned to another certificate refuses this server, as it would refuse an impostor.
        using (var pinnedToAnother = new HttpClient(CertificatePin.CreateHandler(CertificatePin.Of(_otherCertificate))))
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => pinnedToAnother.GetStringAsync(address));
        }

        // Without a pin Windows decides, and it does not trust a self-signed certificate.
        using var unpinned = new HttpClient(CertificatePin.CreateHandler(null));
        await Assert.ThrowsAsync<HttpRequestException>(() => unpinned.GetStringAsync(address));
    }

    [Fact]
    public void MachineWideIdentity_IsKeptUnderProgramData_ForSystemAndAdministratorsOnly()
    {
        var userPath = ProtectedFileIdentityStore.GetDefaultFilePath(machineWide: false);
        var machinePath = ProtectedFileIdentityStore.GetDefaultFilePath(machineWide: true);

        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), userPath);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), machinePath);
        Assert.EndsWith(Path.Combine("NetworkMonitoringSystem", "Agent", "identity.bin"), machinePath);

        var security = ProtectedFileIdentityStore.CreateRestrictedFolderSecurity();
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        // Nothing is inherited from ProgramData, where every user may read.
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
            Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
        });
        Assert.Equal(
            new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid }
                .Select(type => new SecurityIdentifier(type, null).Value)
                .Order(),
            rules.Select(rule => rule.IdentityReference.Value).Order());
    }

    [Fact]
    public void MachineWideIdentity_WrittenByOneStore_IsReadByAnother_AndIsNotPlainText()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nms-tests-" + Guid.NewGuid().ToString("N"));
        var options = Options.Create(new AgentOptions { MachineWideIdentity = true, IdentityFilePath = Path.Combine(directory, "identity.bin") });
        var identity = new AgentIdentity(Guid.NewGuid().ToString(), "secret-agent-key");

        try
        {
            new ProtectedFileIdentityStore(options, NullLogger<ProtectedFileIdentityStore>.Instance).Save(identity);

            Assert.Equal(identity, new ProtectedFileIdentityStore(options, NullLogger<ProtectedFileIdentityStore>.Instance).Load());
            Assert.DoesNotContain("secret-agent-key", File.ReadAllText(options.Value.IdentityFilePath!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string GetAddress(WebApplication server)
    {
        return server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    private static async Task<WebApplication> StartHttpsServerAsync(X509Certificate2 certificate)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrelHttpsConfiguration();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate)));

        var server = builder.Build();
        server.MapGet("/", () => "ok");
        await server.StartAsync();

        return server;
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // Reloaded from PFX so that Windows can use its private key for a TLS connection.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }
}
