using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Optimizer.WinUI.Controls;

/// <summary>
/// Lightweight sparkline that plots an ObservableCollection&lt;double&gt; as a polyline
/// on a Canvas.  Values are assumed to be in the 0–100 range (percent).
/// </summary>
public sealed partial class SparklineChart : UserControl
{
    // ── Dependency properties ──────────────────────────────────────────────

    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(ObservableCollection<double>),
            typeof(SparklineChart), new PropertyMetadata(null, OnValuesChanged));

    public static readonly DependencyProperty LineColorProperty =
        DependencyProperty.Register(nameof(LineColor), typeof(Color),
            typeof(SparklineChart), new PropertyMetadata(Colors.CornflowerBlue, OnLineColorChanged));

    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double),
            typeof(SparklineChart), new PropertyMetadata(1.5, OnRenderPropertyChanged));

    public static readonly DependencyProperty FillOpacityProperty =
        DependencyProperty.Register(nameof(FillOpacity), typeof(double),
            typeof(SparklineChart), new PropertyMetadata(0.15, OnRenderPropertyChanged));

    // ── CLR wrappers ───────────────────────────────────────────────────────

    public ObservableCollection<double>? Values
    {
        get => (ObservableCollection<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    public double FillOpacity
    {
        get => (double)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    // ── Internal state ─────────────────────────────────────────────────────

    private Polyline? _line;
    private Polygon? _fill;
    private ObservableCollection<double>? _subscribedValues;

    public SparklineChart()
    {
        InitializeComponent();
        Loaded += (_, _) => Redraw();
    }

    // ── Change callbacks ───────────────────────────────────────────────────

    private static void OnValuesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SparklineChart)d;

        // Unsubscribe old
        if (chart._subscribedValues != null)
            chart._subscribedValues.CollectionChanged -= chart.OnCollectionChanged;

        chart._subscribedValues = e.NewValue as ObservableCollection<double>;

        // Subscribe new
        if (chart._subscribedValues != null)
            chart._subscribedValues.CollectionChanged += chart.OnCollectionChanged;

        chart.Redraw();
    }

    private static void OnLineColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (SparklineChart)d;
        if (chart._line != null)
            chart._line.Stroke = new SolidColorBrush((Color)e.NewValue);
        if (chart._fill != null)
            chart._fill.Fill = chart.MakeFillBrush();
    }

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((SparklineChart)d).Redraw();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Redraw();

    private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => Redraw();

    // ── Drawing ────────────────────────────────────────────────────────────

    private void Redraw()
    {
        var canvas = ChartCanvas;
        if (canvas == null) return;

        double w = canvas.ActualWidth;
        double h = canvas.ActualHeight;

        if (w < 2 || h < 2) return;

        var data = Values;
        if (data == null || data.Count == 0)
        {
            canvas.Children.Clear();
            _line = null;
            _fill = null;
            return;
        }

        // Build point arrays
        int n = data.Count;
        double stepX = w / Math.Max(1, n - 1);

        var linePoints = new PointCollection();
        var fillPoints = new PointCollection();

        // Fill polygon starts at bottom-left
        fillPoints.Add(new Windows.Foundation.Point(0, h));

        for (int i = 0; i < n; i++)
        {
            double x = i == n - 1 ? w : i * stepX;
            double v = Math.Clamp(data[i], 0, 100);
            double y = h - (v / 100.0) * h;
            linePoints.Add(new Windows.Foundation.Point(x, y));
            fillPoints.Add(new Windows.Foundation.Point(x, y));
        }

        // Fill polygon ends at bottom-right
        fillPoints.Add(new Windows.Foundation.Point(w, h));

        // Recreate shapes if missing or canvas was cleared
        if (_fill == null || !canvas.Children.Contains(_fill))
        {
            _fill = new Polygon
            {
                StrokeThickness = 0,
                Fill = MakeFillBrush(),
            };
            canvas.Children.Clear();
            canvas.Children.Add(_fill);
            _line = null;
        }

        if (_line == null || !canvas.Children.Contains(_line))
        {
            _line = new Polyline
            {
                Stroke = new SolidColorBrush(LineColor),
                StrokeThickness = LineThickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            canvas.Children.Add(_line);
        }

        _fill.Points = fillPoints;
        _line.Points = linePoints;

        // Keep on top
        Canvas.SetZIndex(_fill, 0);
        Canvas.SetZIndex(_line, 1);
    }

    private Brush MakeFillBrush()
    {
        var c = LineColor;
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(0, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb((byte)(255 * FillOpacity), c.R, c.G, c.B), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(0, c.R, c.G, c.B), Offset = 1 },
            }
        };
    }
}
