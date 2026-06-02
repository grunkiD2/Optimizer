using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IIntelligenceService
{
    Task<float?> PredictAcceptanceAsync(string category, string severity);
    Task<IReadOnlyList<AnomalyAlert>> DetectAnomaliesAsync(IReadOnlyList<double> recentValues, string metricName);
    Task TrainAsync();
    bool IsTrained { get; }
    DateTime? LastTrainedAt { get; }
}
