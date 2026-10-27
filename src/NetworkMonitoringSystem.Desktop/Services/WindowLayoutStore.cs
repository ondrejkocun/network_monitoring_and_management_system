using System.IO;
using System.Text.Json;
using System.Windows;

namespace NetworkMonitoringSystem.Desktop.Services;

/// <summary>How the user left the main window: where it was, how big, how the list and the details shared it.</summary>
/// <param name="ListShare">The part of the height the device list had, 0 to 1; the details had the rest.</param>
/// <param name="DetailTab">The index of the tab of the details that was open.</param>
public sealed record WindowLayout(double Left, double Top, double Width, double Height, bool IsMaximized, double ListShare, int DetailTab)
{
    /// <summary>
    /// The layout made to fit the screens there are now: a window left on a monitor that is gone comes back to the
    /// visible area, one larger than the screens shrinks, and an odd share of the list falls back to a third.
    /// </summary>
    public WindowLayout FitTo(Rect screens)
    {
        var width = Math.Clamp(Width, 400, Math.Max(400, screens.Width));
        var height = Math.Clamp(Height, 300, Math.Max(300, screens.Height));

        return this with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(Left, screens.Left, Math.Max(screens.Left, screens.Right - width)),
            Top = Math.Clamp(Top, screens.Top, Math.Max(screens.Top, screens.Bottom - height)),
            ListShare = double.IsFinite(ListShare) && ListShare is > 0.1 and < 0.9 ? ListShare : 1.0 / 3,
            DetailTab = Math.Max(0, DetailTab),
        };
    }
}

/// <summary>
/// Keeps the layout of the main window in a file of the user, so the application opens as it was left.
/// It holds nothing secret; a missing or damaged file just means the default layout.
/// </summary>
public sealed class WindowLayoutStore
{
    private readonly string _filePath;

    public WindowLayoutStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkMonitoringSystem",
            "Desktop",
            "window.json");
    }

    public WindowLayout? Load()
    {
        try
        {
            return File.Exists(_filePath) ? JsonSerializer.Deserialize<WindowLayout>(File.ReadAllText(_filePath)) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Saves the layout; when that fails the next start simply uses the default one.</summary>
    public void Save(WindowLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(layout));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Remembering the window is a convenience; closing the application must not fail because of it.
        }
    }
}
