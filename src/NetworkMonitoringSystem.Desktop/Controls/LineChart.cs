using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Controls;

/// <summary>
/// A line chart of a value between 0 and 100 percent over a period. A null among the points breaks the line.
/// </summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points),
        typeof(IReadOnlyList<ChartPoint?>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LabelWidth = 44;
    private const double VerticalPadding = 8;

    private static readonly Pen GridPen = CreateFrozenPen(Brushes.Gainsboro, 1);
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static readonly int[] GridLevels = [0, 50, 100];

    public IReadOnlyList<ChartPoint?>? Points
    {
        get => (IReadOnlyList<ChartPoint?>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var plot = new Rect(
            LabelWidth,
            VerticalPadding,
            Math.Max(0, ActualWidth - LabelWidth - 4),
            Math.Max(0, ActualHeight - (2 * VerticalPadding)));

        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var level in GridLevels)
        {
            var y = plot.Bottom - (plot.Height * level / 100.0);
            drawingContext.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var label = new FormattedText(
                $"{level} %",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                Brushes.Gray,
                pixelsPerDip);
            drawingContext.DrawText(label, new Point(plot.Left - label.Width - 6, y - (label.Height / 2)));
        }

        if (Points is not { Count: > 0 } points)
        {
            return;
        }

        var pen = new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round };
        var stretch = new List<Point>();

        foreach (var point in points)
        {
            if (point is { } value)
            {
                stretch.Add(new Point(plot.Left + (plot.Width * value.X), plot.Bottom - (plot.Height * value.Y / 100.0)));
            }
            else
            {
                DrawStretch(drawingContext, pen, stretch);
                stretch.Clear();
            }
        }

        DrawStretch(drawingContext, pen, stretch);
    }

    private void DrawStretch(DrawingContext drawingContext, Pen pen, List<Point> stretch)
    {
        if (stretch.Count == 0)
        {
            return;
        }

        // A single measurement has no line to draw, so it is shown as a dot.
        if (stretch.Count == 1)
        {
            drawingContext.DrawEllipse(Stroke, null, stretch[0], 2, 2);

            return;
        }

        var geometry = new StreamGeometry();

        using (var context = geometry.Open())
        {
            context.BeginFigure(stretch[0], isFilled: false, isClosed: false);
            context.PolyLineTo(stretch.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, pen, geometry);
    }

    private static Pen CreateFrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();

        return pen;
    }
}
