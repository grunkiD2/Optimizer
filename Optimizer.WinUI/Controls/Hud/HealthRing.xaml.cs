using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Circular system-health gauge: a glowing accent arc swept to <see cref="Score"/> (0–100),
/// colored by health band (green / amber / red), with a big monospace readout.</summary>
public sealed partial class HealthRing : UserControl
{
    private const double Cx = 86, Cy = 86, R = 70; // matches the 140px track centered in the 172px grid

    public HealthRing()
    {
        InitializeComponent();
        UpdateArc();
        Loaded += (_, _) => GlowPulse.Begin();
    }

    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }
    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(HealthRing),
            new PropertyMetadata(0.0, (d, _) => ((HealthRing)d).UpdateArc()));

    private void UpdateArc()
    {
        double score = Math.Clamp(Score, 0, 100);
        ScoreText.Text = ((int)Math.Round(score)).ToString();

        var status = score >= 75 ? HudStatus.Success
                   : score >= 50 ? HudStatus.Warning
                   : HudStatus.Danger;
        var brush = HudBrushes.Fill(status);
        Arc.Stroke = brush;
        ScoreText.Foreground = brush;

        double pct = score / 100.0;
        if (pct <= 0) { Arc.Data = null; return; }
        double sweep = pct >= 1 ? 359.999 : pct * 360.0;

        var figure = new PathFigure { StartPoint = PointAt(0), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(sweep),
            Size = new Size(R, R),
            IsLargeArc = sweep > 180,
            SweepDirection = SweepDirection.Clockwise,
            RotationAngle = 0,
        });
        var geo = new PathGeometry();
        geo.Figures.Add(figure);
        Arc.Data = geo;
    }

    // Angle measured clockwise from the top (12 o'clock).
    private static Point PointAt(double angleDeg)
    {
        double rad = angleDeg * Math.PI / 180.0;
        return new Point(Cx + R * Math.Sin(rad), Cy - R * Math.Cos(rad));
    }
}
