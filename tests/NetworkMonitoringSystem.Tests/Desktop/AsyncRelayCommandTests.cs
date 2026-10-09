namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Desktop.Commands;

public class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecuteAsync_RunsAction()
    {
        var executed = 0;
        var command = new AsyncRelayCommand(() =>
        {
            executed++;

            return Task.CompletedTask;
        });

        await command.ExecuteAsync();

        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenCanExecuteReturnsFalse()
    {
        var executed = 0;
        var command = new AsyncRelayCommand(
            () =>
            {
                executed++;

                return Task.CompletedTask;
            },
            () => false);

        await command.ExecuteAsync();

        Assert.False(command.CanExecute(null));
        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task Command_IsDisabledWhileRunning_SoItCannotStartTwice()
    {
        var release = new TaskCompletionSource();
        var executed = 0;
        var command = new AsyncRelayCommand(() =>
        {
            executed++;

            return release.Task;
        });
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        var running = command.ExecuteAsync();

        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync();
        Assert.Equal(1, executed);

        release.SetResult();
        await running;

        Assert.True(command.CanExecute(null));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Command_IsEnabledAgain_WhenActionFails()
    {
        var command = new AsyncRelayCommand(() => throw new InvalidOperationException("failed"));

        await Assert.ThrowsAsync<InvalidOperationException>(command.ExecuteAsync);

        Assert.True(command.CanExecute(null));
    }
}
