namespace NetworkMonitoringSystem.Tests.Desktop;

using NetworkMonitoringSystem.Desktop.Commands;

public class RelayCommandTests
{
    [Fact]
    public void Execute_InvokesAction_WhenCanExecuteIsNotProvided()
    {
        var executed = 0;
        var command = new RelayCommand(() => executed++);

        command.Execute(null);

        Assert.True(command.CanExecute(null));
        Assert.Equal(1, executed);
    }

    [Fact]
    public void Execute_DoesNotInvokeAction_WhenCanExecuteReturnsFalse()
    {
        var executed = 0;
        var command = new RelayCommand(() => executed++, () => false);

        command.Execute(null);

        Assert.False(command.CanExecute(null));
        Assert.Equal(0, executed);
    }

    [Fact]
    public void RaiseCanExecuteChanged_RaisesCanExecuteChanged()
    {
        var command = new RelayCommand(() => { });
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }

    [Fact]
    public void Constructor_Throws_WhenExecuteIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }
}
