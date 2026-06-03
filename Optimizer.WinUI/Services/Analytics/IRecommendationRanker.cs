using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Re-ranks recommendations using context-specific learning signals
/// (tool success rates and user feedback) layered on top of severity.
/// </summary>
public interface IRecommendationRanker
{
    /// <summary>Return the recommendations reordered for the given context.</summary>
    Task<IReadOnlyList<Recommendation>> RankAsync(IReadOnlyList<Recommendation> recommendations, string context);
}
