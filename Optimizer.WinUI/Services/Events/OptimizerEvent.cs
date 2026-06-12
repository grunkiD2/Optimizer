namespace Optimizer.WinUI.Services.Events;

public enum OptimizerEventType
{
    OptimizationApplied,
    OptimizationUndone,
    ProfileApplied,
    PluginInstalled,
    PluginEnabled,
    AnomalyDetected,
    ThresholdCrossed,     // e.g. CPU temp > limit, disk > 90%
    DiagnosticCompleted
}

public record OptimizerEvent(
    OptimizerEventType Type,
    string Title,
    string Detail,
    DateTime TimestampUtc,
    IReadOnlyDictionary<string, string>? Data = null)
{
    public static OptimizerEvent Create(
        OptimizerEventType type,
        string title,
        string detail,
        IReadOnlyDictionary<string, string>? data = null)
        => new(type, title, detail, DateTime.UtcNow, data);
}
