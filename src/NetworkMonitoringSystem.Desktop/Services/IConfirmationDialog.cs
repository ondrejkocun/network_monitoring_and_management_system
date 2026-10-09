using System.Windows;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>
/// Asks the user to confirm an action that cannot be undone. View models use this interface
/// instead of showing a window themselves, so they stay testable.
/// </summary>
public interface IConfirmationDialog
{
    bool Confirm(string title, string question);
}

public sealed class MessageBoxConfirmationDialog : IConfirmationDialog
{
    public bool Confirm(string title, string question)
    {
        var result = MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}
