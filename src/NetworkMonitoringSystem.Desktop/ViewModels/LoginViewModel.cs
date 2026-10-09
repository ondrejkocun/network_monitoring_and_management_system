using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;

namespace NetworkMonitoringSystem.Desktop.ViewModels;

/// <summary>
/// The sign-in form. The password is not kept here: the view hands it over only for the attempt itself.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private readonly IServerClient _server;

    private string _userName = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;

    public LoginViewModel(IServerClient server)
    {
        ArgumentNullException.ThrowIfNull(server);

        _server = server;
    }

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    /// <summary>Why the last attempt failed, or why the user has to sign in again; empty when there is nothing to say.</summary>
    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
            }
        }
    }

    public bool CanSubmit => !IsBusy;

    /// <summary>The session of the user who signed in, or null until someone does.</summary>
    public LoginReply? Session { get; private set; }

    /// <returns>True when the user is signed in.</returns>
    public async Task<bool> LoginAsync(string password)
    {
        if (IsBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(password))
        {
            Message = "Zadajte meno aj heslo.";

            return false;
        }

        IsBusy = true;
        Message = "Prihlasujem…";

        try
        {
            Session = await _server.LoginAsync(UserName.Trim(), password);
            Message = string.Empty;

            return true;
        }
        catch (ServerClientException exception)
        {
            Message = exception.Kind switch
            {
                ServerErrorKind.NotSignedIn => "Nesprávne meno alebo heslo. Po viacerých chybných pokusoch sa účet na niekoľko minút zamkne.",
                ServerErrorKind.Unavailable => "Server nie je dostupný.",
                _ => $"Chyba servera: {exception.Message}",
            };

            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
