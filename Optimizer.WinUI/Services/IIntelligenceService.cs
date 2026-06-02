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

/// <summary>
/// Summary statistics of the local per-user personalization model.
/// Safe to keep on-device (raw) or to share after DP noise has been applied (privatized).
/// </summary>
public record LocalModelSummary(
    /// <summary>Per-category acceptance rate (accepts / total interactions). Raw or DP-noised.</summary>
    IReadOnlyDictionary<string, double> CategoryAcceptanceRates,
    /// <summary>Total number of interactions used to compute the rates. Raw or DP-noised count.</summary>
    int TotalSamples,
    /// <summary>UTC time when this summary was computed.</summary>
    DateTime ComputedUtc);

public interface IIntelligenceService
{
    Task<float?> PredictAcceptanceAsync(string category, string severity);
    Task<IReadOnlyList<AnomalyAlert>> DetectAnomaliesAsync(IReadOnlyList<double> recentValues, string metricName);
    Task<AnomalyAnalysis> AnalyzeSeriesAsync(IReadOnlyList<double> values, string metricName);
    Task TrainAsync();
    bool IsTrained { get; }
    DateTime? LastTrainedAt { get; }

    /// <summary>
    /// Computes raw per-category acceptance rates from local preference history.
    /// The result is NEVER sent off-device; use ComputePrivatizedSummary for anything shared.
    /// </summary>
    LocalModelSummary ComputeLocalSummary();

    /// <summary>
    /// Computes the same statistics as ComputeLocalSummary but applies Laplace differential-
    /// privacy noise to each rate and the total sample count before returning.
    /// The result is safe to upload as a federated-learning contribution.
    /// </summary>
    /// <param name="epsilon">Privacy budget. Lower = more privacy. Typical: 1.0.</param>
    LocalModelSummary ComputePrivatizedSummary(double epsilon);
}
