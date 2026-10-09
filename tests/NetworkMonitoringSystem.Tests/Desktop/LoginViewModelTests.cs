namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Contracts.Admin;
using NetworkMonitoringSystem.Desktop.Services;
using NetworkMonitoringSystem.Desktop.ViewModels;

public class LoginViewModelTests
{
    private readonly MainWindowViewModelTests.FakeServerClient _server = new();

    [Fact]
    public async Task Login_WithRightPassword_SignsIn_WithTrimmedName()
    {
        var viewModel = new LoginViewModel(_server) { UserName = " admin " };

        Assert.True(await viewModel.LoginAsync("correct-password"));

        Assert.Equal("admin", viewModel.Session!.UserName);
        Assert.Equal(["admin"], _server.LoginAttempts);
        Assert.Equal(string.Empty, viewModel.Message);
        Assert.True(viewModel.CanSubmit);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ShowsReason_AndDoesNotSignIn()
    {
        var viewModel = new LoginViewModel(_server) { UserName = "admin" };

        Assert.False(await viewModel.LoginAsync("wrong"));

        Assert.Null(viewModel.Session);
        Assert.Contains("Nesprávne meno alebo heslo", viewModel.Message);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("  ", "password")]
    [InlineData("admin", "")]
    public async Task Login_WithoutNameOrPassword_DoesNotCallTheServer(string userName, string password)
    {
        var viewModel = new LoginViewModel(_server) { UserName = userName };

        Assert.False(await viewModel.LoginAsync(password));

        Assert.Empty(_server.LoginAttempts);
        Assert.Equal("Zadajte meno aj heslo.", viewModel.Message);
    }

    [Fact]
    public async Task Login_WhenServerIsUnavailable_SaysSo()
    {
        _server.Failure = new ServerClientException(ServerErrorKind.Unavailable, "connection refused");
        var viewModel = new LoginViewModel(_server) { UserName = "admin" };

        Assert.False(await viewModel.LoginAsync("correct-password"));

        Assert.Equal("Server nie je dostupný.", viewModel.Message);
    }

    [Fact]
    public async Task Logout_EndsTheSession_WithoutAMessage()
    {
        var viewModel = new MainWindowViewModel(_server, new AlwaysConfirm()) { CurrentUserName = "admin" };
        await _server.LoginAsync("admin", "correct-password");
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;

        await viewModel.LogoutCommand.ExecuteAsync();

        Assert.Equal("Prihlásený: admin", viewModel.CurrentUserText);
        Assert.False(_server.IsSignedIn);
        Assert.Equal(1, ended);
        Assert.Equal(string.Empty, viewModel.SessionEndMessage);
    }

    [Fact]
    public async Task ExpiredSession_EndsTheSessionOnce_AndTellsTheUserWhy()
    {
        _server.Devices.Add(new DeviceInfo { Id = "device-1", Name = "PC-01" });
        var viewModel = new MainWindowViewModel(_server, new AlwaysConfirm());
        await viewModel.RefreshAsync();
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        _server.Failure = new ServerClientException(ServerErrorKind.NotSignedIn, "Sign in to use the administration API.");

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        Assert.Equal(1, ended);
        Assert.Contains("Prihláste sa znova", viewModel.SessionEndMessage);
    }

    [Fact]
    public async Task RefusedOperation_IsExplained_AndDoesNotEndTheSession()
    {
        var viewModel = new MainWindowViewModel(_server, new AlwaysConfirm()) { NewDeviceName = "Router", NewDeviceIpAddress = "192.168.50.1" };
        var ended = 0;
        viewModel.SessionEnded += (_, _) => ended++;
        _server.Failure = new ServerClientException(ServerErrorKind.AccessDenied, "You do not have permission for this operation.");

        await viewModel.AddDeviceCommand.ExecuteAsync();

        Assert.Equal("Na túto operáciu nemáte oprávnenie.", viewModel.StatusMessage);
        Assert.Equal(0, ended);
    }

    private sealed class AlwaysConfirm : IConfirmationDialog
    {
        public bool Confirm(string title, string question) => true;
    }
}
