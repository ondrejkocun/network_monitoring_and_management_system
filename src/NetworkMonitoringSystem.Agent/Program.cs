using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Commands;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Agent.Metrics;
using NetworkMonitoringSystem.Agent.Traffic;
using NetworkMonitoringSystem.Contracts.Agents;
using NetworkMonitoringSystem.Contracts.Security;

// "register" registers this computer with the server once and exits. The installer runs it before it creates
// the service, so the enrollment token never has to be stored where the service could read it later.
var registerOnly = args.Length > 0 && string.Equals(args[0], AgentCommandLine.Register, StringComparison.OrdinalIgnoreCase);
var machineWide = registerOnly || WindowsServiceHelpers.IsWindowsService();

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = registerOnly ? args[1..] : args,

    // The settings lie next to the program, wherever it is started from; a service is started
    // in the Windows system directory.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddWindowsService(options => options.ServiceName = AgentCommandLine.ServiceName);

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .PostConfigure(options => options.MachineWideIdentity |= machineWide)
    .Validate(
        options => Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
        $"{AgentOptions.SectionName}:{nameof(AgentOptions.ServerUrl)} must be an https address of the server.")
    .Validate(
        options => AgentCommandLine.IsValidPin(options.ServerCertificateSha256),
        $"{AgentOptions.SectionName}:{nameof(AgentOptions.ServerCertificateSha256)} must be the SHA-256 hash of the server's certificate (64 hexadecimal digits).")
    .ValidateOnStart();

builder.Services
    .AddGrpcClient<AgentApi.AgentApiClient>((services, options) =>
    {
        options.Address = new Uri(services.GetRequiredService<IOptions<AgentOptions>>().Value.ServerUrl);
    })
    .ConfigurePrimaryHttpMessageHandler(services =>
        CertificatePin.CreateHandler(services.GetRequiredService<IOptions<AgentOptions>>().Value.ServerCertificateSha256));

builder.Services.AddSingleton<IAgentIdentityStore, ProtectedFileIdentityStore>();
builder.Services.AddSingleton<ISystemMetricsCollector, WindowsSystemMetricsCollector>();
builder.Services.AddSingleton<ISystemActivityCollector, WindowsSystemActivityCollector>();
builder.Services.AddSingleton<ITrafficCapture, TsharkTrafficCapture>();
builder.Services.AddSingleton<AgentReporter>();
builder.Services.AddSingleton<SynchronizationSignal>();
builder.Services.AddSingleton<ISystemProgramRunner, SystemProgramRunner>();
builder.Services.AddSingleton<ICommandExecutor, WindowsCommandExecutor>();
builder.Services.AddSingleton<IFileReceiver, InboxFileReceiver>();

if (!registerOnly)
{
    builder.Services.AddHostedService<Worker>();
    builder.Services.AddHostedService<CommandWorker>();
}

var host = builder.Build();

if (registerOnly)
{
    return await AgentCommandLine.RegisterAsync(host.Services);
}

host.Run();

return 0;
