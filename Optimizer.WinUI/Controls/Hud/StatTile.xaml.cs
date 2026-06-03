using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>A telemetry tile: micro label, big monospace value + unit, a sparkline, and a caption.
/// <see cref="Status"/> tints the value and sparkline.</summary>
public sealed partial class StatTile : UserControl
{
    public StatTile()
    {
        InitializeComponent();
        ApplyStatus();
        HoverLift.Attach(this);
    }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public static readonly DependencyProperty CaptionProperty =
        DependencyProperty.Register(nameof(Caption), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    public HudStatus Status { get => (HudStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(HudStatus), typeof(StatTile),
            new PropertyMetadata(HudStatus.Accent, (d, _) => ((StatTile)d).ApplyStatus()));

    private void ApplyStatus()
    {
        var brush = HudBrushes.Fill(Status);
        ValueText.Foreground = brush;
        Spark.Stroke = brush;
    }

    /// <summary>Feed a rolling series; rendered as a normalized sparkline (Stretch=Fill).</summary>
    public void SetTrend(IReadOnlyList<double> values)
    {
        if (values is null || values.Count < 2) { Spark.Visibility = Visibility.Collapsed; return; }

        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values) { if (v < min) min = v; if (v > max) max = v; }
        double range = max - min;

        var pts = new PointCollection();
        for (int i = 0; i < values.Count; i++)
        {
            double y = range > 1e-9 ? 1.0 - (values[i] - min) / range : 0.5;
            pts.Add(new Point(i, y));
        }
        Spark.Points = pts;
        Spark.Visibility = Visibility.Visible;
    }
}
