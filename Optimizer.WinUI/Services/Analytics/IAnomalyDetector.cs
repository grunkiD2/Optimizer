namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Learns a per-context baseline for system metrics and flags readings that deviate
/// beyond a sigma threshold — while suppressing metrics the user keeps dismissing.
/// </summary>
public interface IAnomalyDetector
{
    /// <summary>Fold one reading into the (context, metric) baseline.</summary>
    Task RecordSampleAsync(string context, string metric, double value);

    /// <summary>
    /// Evaluate readings for the context; returns anomalies (deviation beyond the
    /// sigma threshold) for metrics that have enough history and aren't suppressed.
    /// Detected anomalies are persisted.
    /// </summary>
    Task<List<AnomalyResult>> EvaluateAsync(string context, IReadOnlyDictionary<string, double> readings);

    /// <summary>Record that the user dismissed alerts for a metric; enough dismissals suppress it.</summary>
    Task DismissAsync(string context, string metric);
}

/// <summary>A detected metric anomaly.</summary>
public class AnomalyResult
{
    public string Context { get; set; } = "";
    public string Metric { get; set; } = "";
    public double Value { get; set; }
    public double Expected { get; set; }
    public double Sigma { get; set; }
    public string Description =>
        $"{Metric} = {Value:F1} in {Context} (expected ~{Expected:F1}, {Sigma:F1}σ off)";
}
