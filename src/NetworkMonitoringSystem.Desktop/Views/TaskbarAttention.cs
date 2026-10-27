using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>Draws the user's attention to a window: its taskbar button flashes until it is activated, with a sound.</summary>
internal static class TaskbarAttention
{
    private const uint FlashTray = 0x2;
    private const uint FlashUntilForeground = 0xC;

    public static void Request(Window window)
    {
        SystemSounds.Exclamation.Play();

        // An active window is already in front of the user; flashing it would only distract.
        if (window.IsActive)
        {
            return;
        }

        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = new WindowInteropHelper(window).Handle,
            Flags = FlashTray | FlashUntilForeground,
        };

        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);
}
