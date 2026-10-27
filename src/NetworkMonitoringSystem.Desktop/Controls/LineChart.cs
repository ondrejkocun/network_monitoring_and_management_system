using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NetworkMonitoringSystem.Desktop.ViewModels;

namespace NetworkMonitoringSystem.Desktop.Controls;

/// <summary>
/// A line chart over a period of up to two series, each a value between 0 and 100 percent of the scale. A null among
/// the points breaks the line. The grid is labelled in percent unless other labels are given.
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

    public static readonly DependencyProperty SecondPointsProperty = DependencyProperty.Register(
        nameof(SecondPoints),
        typeof(IReadOnlyList<ChartPoint?>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SecondStrokeProperty = DependencyProperty.Register(
        nameof(SecondStroke),
        typeof(Brush),
        typeof(LineChart),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Labels of the grid lines at 0, 50 and 100 percent of the scale.</summary>
    public static readonly DependencyProperty GridLabelsProperty = DependencyProperty.Register(
        nameof(GridLabels),
        typeof(IReadOnlyList<string>),
        typeof(LineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double LabelWidth = 64;
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

    public IReadOnlyList<ChartPoint?>? SecondPoints
    {
        get => (IReadOnlyList<ChartPoint?>?)GetValue(SecondPointsProperty);
        set => SetValue(SecondPointsProperty, value);
    }

    public Brush SecondStroke
    {
        get => (Brush)GetValue(SecondStrokeProperty);
        set => SetValue(SecondStrokeProperty, value);
    }

    public IReadOnlyList<string>? GridLabels
    {
        get => (IReadOnlyList<string>?)GetValue(GridLabelsProperty);
        set => SetValue(GridLabelsProperty, value);
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

        for (var index = 0; index < GridLevels.Length; index++)
        {
            var level = GridLevels[index];
            var y = plot.Bottom - (plot.Height * level / 100.0);
            drawingContext.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));

            var label = new FormattedText(
                GridLabels is { Count: 3 } labels ? labels[index] : $"{level} %",
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                11,
                Brushes.Gray,
                pixelsPerDip);
            drawingContext.DrawText(label, new Point(plot.Left - label.Width - 6, y - (label.Height / 2)));
        }

        DrawSeries(drawingContext, plot, Points, Stroke);
        DrawSeries(drawingContext, plot, SecondPoints, SecondStroke);
    }

    private static void DrawSeries(DrawingContext drawingContext, Rect plot, IReadOnlyList<ChartPoint?>? points, Brush stroke)
    {
        if (points is not { Count: > 0 })
        {
            return;
        }

        var pen = new Pen(stroke, 1.5) { LineJoin = PenLineJoin.Round };
        var stretch = new List<Point>();

        foreach (var point in points)
        {
            if (point is { } value)
            {
                stretch.Add(new Point(plot.Left + (plot.Width * value.X), plot.Bottom - (plot.Height * value.Y / 100.0)));
            }
            else
            {
                DrawStretch(drawingContext, pen, stroke, stretch);
                stretch.Clear();
            }
        }

        DrawStretch(drawingContext, pen, stroke, stretch);
    }

    private static void DrawStretch(DrawingContext drawingContext, Pen pen, Brush stroke, List<Point> stretch)
    {
        if (stretch.Count == 0)
        {
            return;
        }

        // A single measurement has no line to draw, so it is shown as a dot.
        if (stretch.Count == 1)
        {
            drawingContext.DrawEllipse(stroke, null, stretch[0], 2, 2);

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
