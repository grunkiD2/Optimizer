using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Optimizer.WinUI.Controls.Hud;

/// <summary>Semantic status used across the HUD controls to pick a token brush.</summary>
public enum HudStatus
{
    Neutral,
    Accent,
    Success,
    Warning,
    Danger,
    Info,
}

/// <summary>Resolves a <see cref="HudStatus"/> to its token brushes (defined top-level in Tokens.xaml).</summary>
internal static class HudBrushes
{
    public static Brush Fill(HudStatus s) => Lookup(s switch
    {
        HudStatus.Accent  => "AccentCyanBrush",
        HudStatus.Success => "SuccessBrush",
        HudStatus.Warning => "WarningBrush",
        HudStatus.Danger  => "DangerBrush",
        HudStatus.Info    => "InfoBrush",
        _                 => "MutedBrush",
    });

    public static Brush Soft(HudStatus s) => Lookup(s switch
    {
        HudStatus.Accent  => "AccentSoftBrush",
        HudStatus.Success => "SuccessSoftBrush",
        HudStatus.Warning => "WarningSoftBrush",
        HudStatus.Danger  => "DangerSoftBrush",
        HudStatus.Info    => "InfoSoftBrush",
        _                 => "MutedSoftBrush",
    });

    private static Brush Lookup(string key) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Brush b
            ? b
            : new SolidColorBrush(Microsoft.UI.Colors.Gray);
}
