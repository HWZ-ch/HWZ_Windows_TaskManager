using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace HWZ.TaskManager.Controls;

public sealed class SparklineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values),
        typeof(IEnumerable<double>),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty Values2Property = DependencyProperty.Register(
        nameof(Values2),
        typeof(IEnumerable<double>),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush),
        typeof(Brush),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Accent2BrushProperty = DependencyProperty.Register(
        nameof(Accent2Brush),
        typeof(Brush),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush),
        typeof(Brush),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowFillProperty = DependencyProperty.Register(
        nameof(ShowFill),
        typeof(bool),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid),
        typeof(bool),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AutoScaleProperty = DependencyProperty.Register(
        nameof(AutoScale),
        typeof(bool),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MinimumRangeProperty = DependencyProperty.Register(
        nameof(MinimumRange),
        typeof(double),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineThicknessProperty = DependencyProperty.Register(
        nameof(LineThickness),
        typeof(double),
        typeof(SparklineChart),
        new FrameworkPropertyMetadata(2.1, FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _collection1;
    private INotifyCollectionChanged? _collection2;

    public SparklineChart()
    {
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public IEnumerable<double>? Values
    {
        get => (IEnumerable<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public IEnumerable<double>? Values2
    {
        get => (IEnumerable<double>?)GetValue(Values2Property);
        set => SetValue(Values2Property, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public Brush Accent2Brush
    {
        get => (Brush)GetValue(Accent2BrushProperty);
        set => SetValue(Accent2BrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public bool ShowFill
    {
        get => (bool)GetValue(ShowFillProperty);
        set => SetValue(ShowFillProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public double MinimumRange
    {
        get => (double)GetValue(MinimumRangeProperty);
        set => SetValue(MinimumRangeProperty, value);
    }

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    private static void OnValuesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var chart = (SparklineChart)dependencyObject;
        if (args.Property == ValuesProperty)
        {
            chart.Detach(chart._collection1);
            chart._collection1 = args.NewValue as INotifyCollectionChanged;
            chart.Attach(chart._collection1);
        }
        else
        {
            chart.Detach(chart._collection2);
            chart._collection2 = args.NewValue as INotifyCollectionChanged;
            chart.Attach(chart._collection2);
        }

        chart.InvalidateVisual();
    }

    private void Attach(INotifyCollectionChanged? collection)
    {
        if (collection is not null)
        {
            collection.CollectionChanged += OnCollectionChanged;
        }
    }

    private void Detach(INotifyCollectionChanged? collection)
    {
        if (collection is not null)
        {
            collection.CollectionChanged -= OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4)
        {
            return;
        }

        const double verticalPadding = 7;
        var chartTop = verticalPadding;
        var chartBottom = height - verticalPadding;
        var chartHeight = Math.Max(1, chartBottom - chartTop);

        var first = Snapshot(Values);
        var second = Snapshot(Values2);
        var maxValue = Math.Max(
            MinimumRange <= 0 ? 1 : MinimumRange,
            AutoScale
                ? Math.Max(first.DefaultIfEmpty(0).Max(), second.DefaultIfEmpty(0).Max()) * 1.08
                : Math.Max(first.DefaultIfEmpty(0).Max(), second.DefaultIfEmpty(0).Max()) * 1.05);

        maxValue = AutoScale ? GetNiceMaximum(maxValue) : maxValue;

        if (ShowGrid)
        {
            var gridPen = new Pen(GridBrush, 1)
            {
                DashStyle = new DashStyle([3, 5], 0)
            };
            for (var index = 1; index <= 3; index++)
            {
                var y = chartTop + chartHeight * index / 4d;
                drawingContext.DrawLine(gridPen, new Point(0, y), new Point(width, y));
            }
        }

        if (first.Count == 0 && second.Count == 0)
        {
            DrawEmpty(drawingContext, width, chartTop, chartHeight);
            return;
        }

        if (ShowFill && first.Count > 1)
        {
            var areaGeometry = BuildGeometry(first, width, chartTop, chartHeight, maxValue, closeToBottom: true);
            var fill = CreateFillBrush(AccentBrush);
            drawingContext.DrawGeometry(fill, null, areaGeometry);
        }

        if (first.Count > 0)
        {
            DrawSeries(drawingContext, first, width, chartTop, chartHeight, maxValue, AccentBrush, LineThickness);
        }

        if (second.Count > 0)
        {
            DrawSeries(drawingContext, second, width, chartTop, chartHeight, maxValue, Accent2Brush, Math.Max(1.5, LineThickness - 0.4));
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        IReadOnlyList<double> values,
        double width,
        double top,
        double height,
        double maxValue,
        Brush brush,
        double thickness)
    {
        if (values.Count == 1)
        {
            var point = PointAt(0, values.Count, values[0], width, top, height, maxValue);
            context.DrawEllipse(brush, null, point, thickness + 1, thickness + 1);
            return;
        }

        var geometry = BuildGeometry(values, width, top, height, maxValue, closeToBottom: false);
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        context.DrawGeometry(null, pen, geometry);
    }

    private static void DrawEmpty(DrawingContext context, double width, double top, double height)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(40, 140, 160, 190)), 1)
        {
            DashStyle = new DashStyle([4, 6], 0)
        };
        var y = top + height / 2;
        context.DrawLine(pen, new Point(12, y), new Point(Math.Max(12, width - 12), y));
    }

    private static Geometry BuildGeometry(
        IReadOnlyList<double> values,
        double width,
        double top,
        double height,
        double maxValue,
        bool closeToBottom)
    {
        var points = new Point[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            points[index] = PointAt(index, values.Count, values[index], width, top, height, maxValue);
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], closeToBottom, closeToBottom);
            AppendCurve(context, points);
            if (closeToBottom)
            {
                context.LineTo(new Point(points[^1].X, top + height + 2), true, false);
                context.LineTo(new Point(points[0].X, top + height + 2), true, false);
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private static void AppendCurve(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        if (points.Count <= 1)
        {
            return;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var beforePrevious = points[Math.Max(0, index - 2)];
            var afterCurrent = points[Math.Min(points.Count - 1, index + 1)];

            var control1 = new Point(
                previous.X + (current.X - beforePrevious.X) / 6d,
                previous.Y + (current.Y - beforePrevious.Y) / 6d);
            var control2 = new Point(
                current.X - (afterCurrent.X - previous.X) / 6d,
                current.Y - (afterCurrent.Y - previous.Y) / 6d);

            context.BezierTo(control1, control2, current, true, false);
        }
    }

    private static Point PointAt(
        int index,
        int count,
        double value,
        double width,
        double top,
        double height,
        double maxValue)
    {
        var x = count <= 1 ? 0 : width * index / (count - 1d);
        var normalized = Math.Clamp(value / Math.Max(0.0001, maxValue), 0, 1);
        var y = top + height - normalized * height;
        return new Point(x, y);
    }

    private static Brush CreateFillBrush(Brush accent)
    {
        var color = accent is SolidColorBrush solid ? solid.Color : Color.FromRgb(77, 141, 255);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(66, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(16, color.R, color.G, color.B), 0.62));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    private static double GetNiceMaximum(double value)
    {
        if (value <= 10)
        {
            return 10;
        }

        if (value <= 25)
        {
            return 25;
        }

        if (value <= 50)
        {
            return 50;
        }

        if (value <= 100)
        {
            return 100;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var nice = normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }

    private static List<double> Snapshot(IEnumerable<double>? values)
    {
        if (values is null)
        {
            return [];
        }

        var result = new List<double>();
        try
        {
            foreach (var value in values)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    result.Add(0);
                }
                else
                {
                    result.Add(Math.Max(0, value));
                }
            }
        }
        catch (InvalidOperationException)
        {
            return result;
        }

        return result;
    }
}
