using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NetworkMonitoringSystem.Desktop.Controls;

/// <summary>Colours a gauge by how full it is: blue, orange above 75 percent, red above 90 percent.</summary>
public sealed class UsageBrushConverter : IValueConverter
{
    private static readonly Brush Normal = Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));
    private static readonly Brush High = Freeze(new SolidColorBrush(Color.FromRgb(0xEF, 0x6C, 0x00)));
    private static readonly Brush Critical = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value is double number ? number : 0;

        return percent > 90 ? Critical : percent > 75 ? High : Normal;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();

        return brush;
    }
}
