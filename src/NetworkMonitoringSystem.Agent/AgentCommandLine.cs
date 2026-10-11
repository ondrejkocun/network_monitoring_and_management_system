using Microsoft.Extensions.Options;
using NetworkMonitoringSystem.Agent.Identity;
using NetworkMonitoringSystem.Contracts.Security;

namespace NetworkMonitoringSystem.Agent;

/// <summary>What the agent does when it is started for a single task instead of running continuously.</summary>
public static class AgentCommandLine
{
    /// <summary>Name of the Windows service the agent is installed as.</summary>
    public const string ServiceName = "NetworkMonitoringAgent";

    /// <summary>The argument that makes the agent register and exit.</summary>
    public const string Register = "register";

    /// <summary>An empty pin is valid: the certificate is then judged by Windows.</summary>
    public static bool IsValidPin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return true;
        }

        try
        {
            CertificatePin.Normalize(pin);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Registers this computer with the server unless it already is registered.</summary>
    /// <returns>The exit code of the program: 0 when the computer is registered, 1 when it is not.</returns>
    public static async Task<int> RegisterAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AgentCommandLine));

        try
        {
            // Reading the options validates them; a wrong server address is reported here and not later.
            _ = services.GetRequiredService<IOptions<AgentOptions>>().Value;
        }
        catch (OptionsValidationException exception)
        {
            logger.LogError("The agent is not configured correctly: {Reason}", string.Join(" ", exception.Failures));

            return 1;
        }

        var identityStore = services.GetRequiredService<IAgentIdentityStore>();

        if (identityStore.Load() is not null)
        {
            logger.LogInformation("This computer is already registered; nothing was changed.");

            return 0;
        }

        // Without an identity, one round of reporting is the registration.
        await services.GetRequiredService<AgentReporter>().ReportOnceAsync(cancellationToken);

        if (identityStore.Load() is null)
        {
            logger.LogError("Registration did not succeed. See the messages above for the reason.");

            return 1;
        }

        return 0;
    }
}
