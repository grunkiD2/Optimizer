namespace Optimizer.WinUI.Models;

public sealed class ConsoleLine
{
    public DateTime TimestampLocal { get; init; } = DateTime.Now;
    public string Glyph { get; init; } = "•";
    public string Text { get; init; } = "";
    public string Color { get; init; } = "#9CA3AF";

    public string TimeText => TimestampLocal.ToString("HH:mm:ss");
}
