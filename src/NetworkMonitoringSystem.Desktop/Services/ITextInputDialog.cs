using System.Windows;
using NetworkMonitoringSystem.Desktop.Views;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>
/// Asks the user for one line of text. View models use this interface instead of showing a window themselves,
/// so they stay testable.
/// </summary>
public interface ITextInputDialog
{
    /// <summary>Returns the text the user confirmed, or null when they cancelled.</summary>
    string? AskForText(string title, string prompt, string initialText, int maxLength);
}

public sealed class TextInputDialog : ITextInputDialog
{
    public string? AskForText(string title, string prompt, string initialText, int maxLength)
    {
        var window = new TextInputWindow(title, prompt, initialText, maxLength)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive),
        };

        return window.ShowDialog() == true ? window.Text : null;
    }
}
