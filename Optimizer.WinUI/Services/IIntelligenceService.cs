using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public enum AnomalyClass { None, Spike, Dip, UpwardTrend, DownwardTrend }

public record AnomalyAnalysis(
    bool IsAnomaly,
    AnomalyClass Class,
    double Score,           // 0-1 confidence
    double LatestValue,
    double ExpectedValue,
    string Description);

public interface IIntelligenceService
{
    Task<float?> PredictAcceptanceAsync(string category, string severity);
    Task<IReadOnlyList<AnomalyAlert>> DetectAnomaliesAsync(IReadOnlyList<double> recentValues, string metricName);
    Task<AnomalyAnalysis> AnalyzeSeriesAsync(IReadOnlyList<double> values, string metricName);
    Task TrainAsync();
    bool IsTrained { get; }
    DateTime? LastTrainedAt { get; }
}
