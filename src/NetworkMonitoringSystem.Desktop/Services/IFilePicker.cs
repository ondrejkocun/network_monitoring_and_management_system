using Microsoft.Win32;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>
/// Lets the user pick a file. View models use this interface instead of showing a dialog themselves, so they stay testable.
/// </summary>
public interface IFilePicker
{
    /// <summary>Returns the full path of the chosen file, or null when the user cancelled.</summary>
    string? PickFileToSend();
}

public sealed class OpenFileDialogPicker : IFilePicker
{
    public string? PickFileToSend()
    {
        var dialog = new OpenFileDialog { Title = "Vyberte súbor na odoslanie", CheckFileExists = true };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
