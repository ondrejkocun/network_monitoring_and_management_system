using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Commands;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>What a new code registers: a new computer, or one existing device again.</summary>
public sealed record EnrollmentTarget(string? DeviceId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A choice of how long a new code is valid.</summary>
public sealed record ValidityOption(string Label, int Minutes)
{
    public override string ToString() => Label;
}

/// <summary>
/// One-time codes with which agents register: creating them for a new computer or for a device to register again,
/// seeing whether and by whom they were used, and revoking the ones not needed any more.
/// </summary>
public sealed class EnrollmentCodesViewModel : ViewModelBase
{
    public const string NewComputerLabel = "Nový počítač";

    private readonly IServerClient _server;
    private readonly IConfirmationDialog _confirmation;
    private IReadOnlyList<EnrollmentCodeRowViewModel> _codes = [];
    private EnrollmentCodeRowViewModel? _selectedCode;
    private EnrollmentTarget _selectedTarget;
    private ValidityOption _selectedValidity;
    private string _note = string.Empty;
    private string _createdCode = string.Empty;
    private string _createdCodeHint = string.Empty;
    private string _message = string.Empty;

    /// <param name="devices">Devices with an agent the user may manage; a code can register any of them again.</param>
    /// <param name="canCreateForNewComputer">True when the user may add devices, which a code for a new computer does.</param>
    /// <param name="preselectedDeviceId">The device to offer first, when the window was opened for one.</param>
    public EnrollmentCodesViewModel(
        IServerClient server,
        IConfirmationDialog confirmation,
        IEnumerable<DeviceRowViewModel> devices,
        bool canCreateForNewComputer,
        string? preselectedDeviceId = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(devices);

        _server = server;
        _confirmation = confirmation;

        var targets = new List<EnrollmentTarget>();

        if (canCreateForNewComputer)
        {
            targets.Add(new EnrollmentTarget(null, NewComputerLabel));
        }

        targets.AddRange(devices
            .Where(device => device.HasAgent && device.CanManage)
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(device => new EnrollmentTarget(device.Id, $"Znova: {device.Name}")));

        Targets = targets;
        _selectedTarget = targets.FirstOrDefault(target => target.DeviceId == preselectedDeviceId) ?? targets.FirstOrDefault()
            ?? new EnrollmentTarget(null, NewComputerLabel);
        _selectedValidity = ValidityOptions[1];

        LoadCommand = new AsyncRelayCommand(LoadAsync);
        CreateCommand = new AsyncRelayCommand(CreateAsync, () => Targets.Count > 0);
        RevokeCommand = new AsyncRelayCommand(RevokeAsync, () => SelectedCode is { IsValid: true });
    }

    public IReadOnlyList<EnrollmentTarget> Targets { get; }

    public IReadOnlyList<ValidityOption> ValidityOptions { get; } =
    [
        new("1 hodina", 60),
        new("24 hodín", 24 * 60),
        new("7 dní", 7 * 24 * 60),
    ];

    public EnrollmentTarget SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedTarget, value);
            }
        }
    }

    public ValidityOption SelectedValidity
    {
        get => _selectedValidity;
        set
        {
            if (value is not null)
            {
                SetProperty(ref _selectedValidity, value);
            }
        }
    }

    /// <summary>What the administrator will recognise the code by, for example where the computer stands.</summary>
    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value ?? string.Empty);
    }

    /// <summary>The code just created; it cannot be shown again, the server keeps only its hash.</summary>
    public string CreatedCode
    {
        get => _createdCode;
        private set
        {
            if (SetProperty(ref _createdCode, value))
            {
                OnPropertyChanged(nameof(HasCreatedCode));
            }
        }
    }

    public bool HasCreatedCode => CreatedCode.Length > 0;

    public string CreatedCodeHint
    {
        get => _createdCodeHint;
        private set => SetProperty(ref _createdCodeHint, value);
    }

    public IReadOnlyList<EnrollmentCodeRowViewModel> Codes
    {
        get => _codes;
        private set => SetProperty(ref _codes, value);
    }

    public EnrollmentCodeRowViewModel? SelectedCode
    {
        get => _selectedCode;
        set
        {
            if (SetProperty(ref _selectedCode, value))
            {
                RevokeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public AsyncRelayCommand LoadCommand { get; }

    public AsyncRelayCommand CreateCommand { get; }

    public AsyncRelayCommand RevokeCommand { get; }

    public async Task LoadAsync()
    {
        try
        {
            var selectedId = SelectedCode?.Id;
            Codes = (await _server.GetEnrollmentCodesAsync()).Select(code => new EnrollmentCodeRowViewModel(code)).ToList();
            SelectedCode = Codes.FirstOrDefault(code => code.Id == selectedId);
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);
        }
    }

    private async Task CreateAsync()
    {
        var target = SelectedTarget;
        CreatedEnrollmentCodeReply created;

        try
        {
            created = await _server.CreateEnrollmentCodeAsync(Note.Trim(), target.DeviceId, SelectedValidity.Minutes);
        }
        catch (ServerClientException exception)
        {
            Message = Describe(exception);

            return;
        }

        CreatedCode = created.Code;
        CreatedCodeHint = target.DeviceId is null
            ? $"Kód pre nový počítač platí do {DeviceHistoryViewModel.FormatTime(created.Info.ExpiresAt.ToDateTimeOffset())} a dá sa použiť raz. "
                + "Zobrazuje sa len teraz; zadajte ho pri inštalácii agenta namiesto registračného tokenu."
            : $"Kód zaregistruje znova zariadenie „{created.Info.DeviceName}“. Platí do {DeviceHistoryViewModel.FormatTime(created.Info.ExpiresAt.ToDateTimeOffset())} "
                + "a dá sa použiť raz. Zobrazuje sa len teraz.";
        Note = string.Empty;
        Message = string.Empty;

        await LoadAsync();
    }

    private async Task RevokeAsync()
    {
        if (SelectedCode is not { IsValid: true } code
            || !_confirmation.Confirm("Zrušiť registračný kód", $"Naozaj chcete zrušiť kód {code.TargetText}? Agent sa ním už nezaregistruje."))
        {
            return;
        }

        try
        {
            await _server.RevokeEnrollmentCodeAsync(code.Id);
            Message = "Kód bol zrušený.";
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
            ServerErrorKind.NotSignedIn => "Prihlásenie vypršalo. Zavrite okno a prihláste sa znova.",
            ServerErrorKind.NotFound => "Zariadenie alebo kód už neexistuje.",
            ServerErrorKind.InvalidInput => $"Server kód odmietol: {exception.Message}",
            _ => $"Chyba servera: {exception.Message}",
        };
    }
}

public sealed class EnrollmentCodeRowViewModel
{
    public EnrollmentCodeRowViewModel(EnrollmentCodeInfo code)
    {
        ArgumentNullException.ThrowIfNull(code);

        Id = code.Id;
        Note = code.Note;
        TargetText = string.IsNullOrEmpty(code.DeviceId)
            ? EnrollmentCodesViewModel.NewComputerLabel.ToLowerInvariant()
            : string.IsNullOrEmpty(code.DeviceName) ? "odstránené zariadenie" : code.DeviceName;
        CreatedText = DeviceHistoryViewModel.FormatTime(code.CreatedAt.ToDateTimeOffset());
        ExpiresText = DeviceHistoryViewModel.FormatTime(code.ExpiresAt.ToDateTimeOffset());
        CreatedBy = code.CreatedBy;
        IsValid = code.State == "Valid";
        StateText = code.State switch
        {
            "Valid" => "Platný",
            "Used" => $"Použitý {DeviceHistoryViewModel.FormatTime(code.UsedAt.ToDateTimeOffset())} ({code.UsedByHostName})",
            "Expired" => "Vypršal",
            "Revoked" => "Zrušený",
            _ => code.State,
        };
    }

    public string Id { get; }

    public string Note { get; }

    /// <summary>"nový počítač", or the device the code registers again.</summary>
    public string TargetText { get; }

    public string CreatedText { get; }

    public string ExpiresText { get; }

    public string CreatedBy { get; }

    public string StateText { get; }

    public bool IsValid { get; }
}
