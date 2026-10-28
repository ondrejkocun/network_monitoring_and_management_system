using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>A choice in a list of the rule form: what the server calls it and what the user reads.</summary>
public sealed record FirewallOption(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// The Firewall tab: the rules of one device with an agent, the form to add one and removing the selected one. The rules
/// are reloaded with the rest of the device, so the state the agent reported appears by itself.
/// </summary>
public sealed class DeviceFirewallViewModel : ViewModelBase
{
    private readonly IServerClient _server;
    private readonly IConfirmationDialog _confirmation;
    private IReadOnlyList<FirewallRuleRowViewModel> _rules = [];
    private FirewallRuleRowViewModel? _selectedRule;
    private string _problem = string.Empty;
    private string _message = string.Empty;
    private string _name = string.Empty;
    private string _ports = string.Empty;
    private string _remoteAddresses = string.Empty;
    private FirewallOption _direction;
    private FirewallOption _protocol;
    private FirewallOption _action;
    private DeviceFirewallReply? _shown;

    /// <param name="canManage">True when the user may change the firewall of this device; otherwise the tab only shows the rules.</param>
    public DeviceFirewallViewModel(IServerClient server, IConfirmationDialog confirmation, string deviceId, bool canManage)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);

        _server = server;
        _confirmation = confirmation;
        DeviceId = deviceId;
        CanManage = canManage;
        _direction = Directions[1];
        _protocol = Protocols[1];
        _action = Actions[0];

        AddCommand = new AsyncRelayCommand(AddAsync, () => CanManage && Name.Trim().Length > 0);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => CanManage && SelectedRule is not null);
    }

    public string DeviceId { get; }

    public bool CanManage { get; }

    public IReadOnlyList<FirewallOption> Directions { get; } = [new("Inbound", "Prichádzajúce"), new("Outbound", "Odchádzajúce")];

    public IReadOnlyList<FirewallOption> Protocols { get; } = [new("Any", "Všetky"), new("Tcp", "TCP"), new("Udp", "UDP"), new("Icmp", "ICMP (ping)")];

    public IReadOnlyList<FirewallOption> Actions { get; } = [new("Block", "Blokovať"), new("Allow", "Povoliť")];

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value ?? string.Empty))
            {
                AddCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public FirewallOption Direction
    {
        get => _direction;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _direction, value);
            }
        }
    }

    public FirewallOption Protocol
    {
        get => _protocol;
        set
        {
            if (value is not null && SetProperty(ref _protocol, value))
            {
                OnPropertyChanged(nameof(ArePortsAllowed));
            }
        }
    }

    public FirewallOption Action
    {
        get => _action;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _action, value);
            }
        }
    }

    /// <summary>Ports make sense only for TCP and UDP; the field is off for the others.</summary>
    public bool ArePortsAllowed => Protocol.Value is "Tcp" or "Udp";

    public string Ports
    {
        get => _ports;
        set => SetProperty(ref _ports, value ?? string.Empty);
    }

    public string RemoteAddresses
    {
        get => _remoteAddresses;
        set => SetProperty(ref _remoteAddresses, value ?? string.Empty);
    }

    public IReadOnlyList<FirewallRuleRowViewModel> Rules
    {
        get => _rules;
        private set
        {
            if (SetProperty(ref _rules, value))
            {
                OnPropertyChanged(nameof(Header));
            }
        }
    }

    public FirewallRuleRowViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                RemoveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Header => Rules.Count == 0 ? "Firewall" : $"Firewall ({Rules.Count})";

    /// <summary>Why the rules may not be in effect, as the agent reported it; empty when there is no problem.</summary>
    public string Problem
    {
        get => _problem;
        private set => SetProperty(ref _problem, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public AsyncRelayCommand AddCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            var reply = await _server.GetDeviceFirewallAsync(DeviceId);

            // Unchanged rules keep their rows, so the selection stays.
            if (reply.Equals(_shown))
            {
                return;
            }

            _shown = reply;
            var selectedId = SelectedRule?.Id;
            Rules = reply.Rules.Select(rule => new FirewallRuleRowViewModel(rule)).ToList();
            SelectedRule = Rules.FirstOrDefault(rule => rule.Id == selectedId);
            Problem = ServerMessages.ToSlovakText(reply.Problem);
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);
        }
    }

    private async Task AddAsync()
    {
        try
        {
            await _server.AddFirewallRuleAsync(new AddFirewallRuleRequest
            {
                DeviceId = DeviceId,
                Name = Name.Trim(),
                Direction = Direction.Value,
                Protocol = Protocol.Value,
                Ports = ArePortsAllowed ? Ports.Trim() : string.Empty,
                RemoteAddresses = RemoteAddresses.Trim(),
                Action = Action.Value,
            });
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);

            return;
        }

        Message = $"Pravidlo „{Name.Trim()}“ bolo pridané; agent ho použije pri najbližšom hlásení.";
        Name = string.Empty;
        Ports = string.Empty;
        RemoteAddresses = string.Empty;
        await LoadAsync();
    }

    private async Task RemoveAsync()
    {
        if (SelectedRule is not { } rule
            || !_confirmation.Confirm("Odstrániť pravidlo firewallu", $"Naozaj chcete odstrániť pravidlo „{rule.Name}“? Agent ho zo zariadenia odstráni pri najbližšom hlásení."))
        {
            return;
        }

        try
        {
            await _server.RemoveFirewallRuleAsync(rule.Id);
            Message = $"Pravidlo „{rule.Name}“ bolo odstránené.";
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);
        }

        await LoadAsync();
    }

    private static string Describe(ServerClientException exception)
    {
        return exception.Kind switch
        {
            ServerErrorKind.Unavailable => "Server nie je dostupný.",
            ServerErrorKind.AccessDenied => "Na túto operáciu nemáte oprávnenie.",
            ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Prihláste sa znova.",
            ServerErrorKind.NotFound => "Zariadenie alebo pravidlo už neexistuje.",
            ServerErrorKind.InvalidInput => $"Server pravidlo odmietol: {exception.Message}",
            _ => $"Chyba servera: {exception.Message}",
        };
    }
}

public sealed class FirewallRuleRowViewModel
{
    public FirewallRuleRowViewModel(FirewallRuleInfo rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Id = rule.Id;
        Name = rule.Name;
        DirectionText = rule.Direction == "Inbound" ? "Prichádzajúce" : "Odchádzajúce";
        ProtocolText = rule.Protocol switch
        {
            "Any" => "Všetky",
            "Icmp" => "ICMP",
            _ => rule.Protocol.ToUpperInvariant(),
        };
        PortsText = string.IsNullOrEmpty(rule.Ports) ? "všetky" : rule.Ports;
        AddressesText = string.IsNullOrEmpty(rule.RemoteAddresses) ? "ľubovoľná" : rule.RemoteAddresses;
        ActionText = rule.Action == "Block" ? "Blokovať" : "Povoliť";
        IsBlock = rule.Action == "Block";
        CreatedBy = rule.CreatedBy;
        StateText = rule.State switch
        {
            "Applied" => "Použité",
            "Failed" => $"Chyba: {ServerMessages.ToSlovakText(rule.StateMessage)}",
            _ => "Čaká na agenta",
        };
        IsFailed = rule.State == "Failed";
    }

    public string Id { get; }

    public string Name { get; }

    public string DirectionText { get; }

    public string ProtocolText { get; }

    public string PortsText { get; }

    public string AddressesText { get; }

    public string ActionText { get; }

    public bool IsBlock { get; }

    public string CreatedBy { get; }

    public string StateText { get; }

    public bool IsFailed { get; }
}
