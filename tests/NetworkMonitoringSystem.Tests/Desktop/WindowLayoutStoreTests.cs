namespace NetworkMonitoringSystem.Tests.Desktop;

using System.Windows;
using NetworkMonitoringSystem.Desktop.Services;

public sealed class WindowLayoutStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nms-layout-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void SavedLayout_IsLoadedBack_AndAMissingOrDamagedFileMeansTheDefault()
    {
        var path = Path.Combine(_folder, "window.json");
        var store = new WindowLayoutStore(path);
        Assert.Null(store.Load());

        var layout = new WindowLayout(100, 50, 1200, 800, IsMaximized: true, ListShare: 0.4, DetailTab: 2);
        store.Save(layout);
        Assert.Equal(layout, store.Load());

        File.WriteAllText(path, "{ not json");
        Assert.Null(store.Load());
    }

    [Fact]
    public void Layout_ComesBackOnScreen_WhenTheMonitorItWasOnIsGone()
    {
        var screen = new Rect(0, 0, 1920, 1080);

        var fitted = new WindowLayout(2500, -300, 3000, 900, false, 0.95, -1).FitTo(screen);

        Assert.Equal((0.0, 0.0, 1920.0, 900.0), (fitted.Left, fitted.Top, fitted.Width, fitted.Height));
        Assert.Equal(1.0 / 3, fitted.ListShare);
        Assert.Equal(0, fitted.DetailTab);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }
}
