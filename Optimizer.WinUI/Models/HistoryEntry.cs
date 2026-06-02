namespace Optimizer.WinUI.Models;

public class HistoryEntry
{
    public string Id { get; } = Guid.NewGuid().ToString();
    public string OptimizationId { get; set; } = "";
    public string OptimizationTitle { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public HistoryAction Action { get; set; }
    public bool IsReversible { get; set; }
    public bool IsUndone { get; set; }
    public string? ResultText { get; set; }
}

public enum HistoryAction
{
    Applied,
    Undone,
    OneTime
}
