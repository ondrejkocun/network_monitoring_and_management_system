using NetworkMonitoringSystem.Agent;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Agents;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps,
        $"{AgentOptions.SectionName}:{nameof(AgentOptions.ServerUrl)} must be an https address of the server.")
    .ValidateOnStart();

builder.Services.AddGrpcClient<AgentApi.AgentApiClient>((services, options) =>
{
    options.Address = new Uri(services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentOptions>>().Value.ServerUrl);
});

builder.Services.AddSingleton<IAgentIdentityStore, ProtectedFileIdentityStore>();
builder.Services.AddSingleton<AgentReporter>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
