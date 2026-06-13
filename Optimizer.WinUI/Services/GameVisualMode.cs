namespace Optimizer.WinUI.Services;

/// <summary>
/// GameVisual modes ↔ the raw VCP 0xDC payload byte (DECIMAL) the Fancontrol engine stores as
/// display.dc. Verified map (docs\debug-CLAUDE-original.md): FPS=7, Racing=5, Cinema=1, sRGB=10.
/// Every other dc value is "unknown" (live data has AAA-HDR.dc=8, D2.dc=150) → shown raw, kept as-is.
/// This map lives ONLY here in the Optimizer (the engine exposes raw dc, no name table).
/// </summary>
public static class GameVisualMode
{
    public record Mode(string Name, int Dc);

    public static readonly IReadOnlyList<Mode> NamedModes =
    [
        new("FPS", 7), new("Racing", 5), new("Cinema", 1), new("sRGB", 10),
    ];

    public static string? NameForDc(int dc) => NamedModes.FirstOrDefault(m => m.Dc == dc)?.Name;

    public static int? DcForName(string name) => NamedModes.FirstOrDefault(m => m.Name == name)?.Dc;

    /// <summary>Display label: the mode name, or "Brugerdefineret (n)" for an unknown dc.</summary>
    public static string Label(int dc) => NameForDc(dc) ?? $"Brugerdefineret ({dc})";
}
