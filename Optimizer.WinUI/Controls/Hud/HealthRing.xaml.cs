using System;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Win2D system-health gauge: a faint track, a soft blurred bloom, and the progress arc
/// (colored by health band), swept to <see cref="Score"/> (0–100), with a monospace readout.</summary>
public sealed partial class HealthRing : UserControl
{
    private const float Cx = 92f, Cy = 92f, R = 64f, Stroke = 11f;

    public HealthRing()
    {
        InitializeComponent();
        Unloaded += (_, _) => Canvas.RemoveFromVisualTree(); // release the Win2D device
        OnScoreChanged();
    }

    public double Score
    {
        get => (double)GetValue(ScoreProperty);
        set => SetValue(ScoreProperty, value);
    }
    public static readonly DependencyProperty ScoreProperty =
        DependencyProperty.Register(nameof(Score), typeof(double), typeof(HealthRing),
            new PropertyMetadata(0.0, (d, _) => ((HealthRing)d).OnScoreChanged()));

    private void OnScoreChanged()
    {
        double score = Math.Clamp(Score, 0, 100);
        ScoreText.Text = ((int)Math.Round(score)).ToString();
        ScoreText.Foreground = HudBrushes.Fill(Band(score));
        Canvas?.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        double score = Math.Clamp(Score, 0, 100);
        Color color = StatusColor(Band(score));

        var caps = new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

        // Faint full-circle track
        ds.DrawCircle(Cx, Cy, R, Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), Stroke);

        if (score <= 0) return;

        float sweep = (float)(score / 100.0 * 2 * Math.PI);
        using var arc = BuildArc(sender, sweep);

        // Soft glow bloom: a blurred copy of the arc, drawn underneath
        using (var commands = new CanvasCommandList(sender))
        {
            using (var clds = commands.CreateDrawingSession())
                clds.DrawGeometry(arc, color, Stroke + 2f, caps);
            using var glow = new GaussianBlurEffect { Source = commands, BlurAmount = 11f };
            ds.DrawImage(glow);
            ds.DrawImage(glow); // double-pass for a brighter bloom
        }

        // Crisp arc on top
        ds.DrawGeometry(arc, color, Stroke, caps);
    }

    private static CanvasGeometry BuildArc(ICanvasResourceCreator rc, float sweepRad)
    {
        using var pb = new CanvasPathBuilder(rc);
        const int segments = 96;
        pb.BeginFigure(PointAt(0));
        for (int i = 1; i <= segments; i++)
            pb.AddLine(PointAt(sweepRad * i / segments));
        pb.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(pb);
    }

    // Angle measured clockwise from the top (12 o'clock).
    private static Vector2 PointAt(float theta) =>
        new(Cx + R * (float)Math.Sin(theta), Cy - R * (float)Math.Cos(theta));

    private static HudStatus Band(double score) =>
        score >= 75 ? HudStatus.Success : score >= 50 ? HudStatus.Warning : HudStatus.Danger;

    private static Color StatusColor(HudStatus s) =>
        HudBrushes.Fill(s) is SolidColorBrush b ? b.Color : Microsoft.UI.Colors.Gray;
}
