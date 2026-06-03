using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>A small full-radius status badge: colored dot + label, tinted by <see cref="Status"/>.</summary>
public sealed partial class StatusPill : UserControl
{
    public StatusPill()
    {
        InitializeComponent();
        UpdateVisual();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(""));

    public HudStatus Status
    {
        get => (HudStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(HudStatus), typeof(StatusPill),
            new PropertyMetadata(HudStatus.Neutral, (d, _) => ((StatusPill)d).UpdateVisual()));

    private void UpdateVisual()
    {
        Root.Background = HudBrushes.Soft(Status);
        Dot.Fill = HudBrushes.Fill(Status);
        LabelText.Foreground = HudBrushes.Fill(Status);
    }
}
