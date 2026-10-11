using NetworkMonitoringSystem.Contracts.Admin;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>A remote command the user can choose to send.</summary>
/// <param name="Name">Name of the command as the server knows it.</param>
/// <param name="ParameterLabel">What the command needs to be told, or null when it needs nothing.</param>
/// <param name="Warning">What sending the command causes, when the user should confirm it first; null otherwise.</param>
public sealed record CommandOption(string Name, string Label, string? ParameterLabel = null, string? Warning = null)
{
    /// <summary>All commands, in the order they are offered.</summary>
    public static IReadOnlyList<CommandOption> All { get; } =
    [
        new("Ping", "Ping zo zariadenia", "Cieľová adresa"),
        new("Traceroute", "Traceroute zo zariadenia", "Cieľová adresa"),
        new("SynchronizeNow", "Okamžitá synchronizácia"),
        new("FlushDnsCache", "Vyprázdnenie DNS cache"),
        new("StartService", "Spustenie služby", "Názov služby"),
        new("StopService", "Zastavenie služby", "Názov služby", "Služba na zariadení sa zastaví."),
        new("RestartService", "Reštart služby", "Názov služby", "Služba na zariadení sa zastaví a znova spustí."),
        new("KillProcess", "Ukončenie procesu", "PID procesu", "Proces sa ukončí okamžite a neuložená práca v ňom sa stratí."),
        new("RestartDevice", "Reštart zariadenia", Warning: "Zariadenie sa o 30 sekúnd reštartuje."),
        new("ShutDownDevice", "Vypnutie zariadenia", Warning: "Zariadenie sa o 30 sekúnd vypne a na diaľku ho už nezapnete."),
    ];

    public bool NeedsParameter => ParameterLabel is not null;

    /// <summary>Returns the name of the command for people, or the server's name for a command this version does not know.</summary>
    public static string Describe(string name) => All.FirstOrDefault(option => option.Name == name)?.Label ?? name;

    /// <summary>Screen readers announce an item of a list by this text.</summary>
    public override string ToString() => Label;
}

/// <summary>One command sent to a device, with how it ended.</summary>
public sealed class CommandRowViewModel
{
    public CommandRowViewModel(CommandInfo command)
    {
        ArgumentNullException.ThrowIfNull(command);

        Id = command.Id;
        RequestedText = DeviceActivityViewModel.FormatTime(command.RequestedAt);
        CommandText = CommandOption.Describe(command.Type);
        Parameter = command.Parameter;
        RequestedBy = command.RequestedBy;
        ResultMessage = command.ResultMessage;

        StatusText = command.Status switch
        {
            "Pending" => "Čaká na agenta",
            "Sent" => "Vykonáva sa",
            "Succeeded" => "Úspešný",
            "Failed" => "Zlyhal",
            "Expired" => "Vypršal",
            _ => command.Status,
        };

        // The first line is enough for the list; the whole result is shown when the command is selected.
        ResultSummary = command.ResultMessage.Split('\n', 2)[0].Trim();
    }

    public string Id { get; }

    public string RequestedText { get; }

    public string CommandText { get; }

    public string Parameter { get; }

    public string RequestedBy { get; }

    public string StatusText { get; }

    public string ResultSummary { get; }

    public string ResultMessage { get; }
}
