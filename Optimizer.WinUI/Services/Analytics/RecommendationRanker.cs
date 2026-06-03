using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Scores each recommendation by combining its base severity with two learned
/// signals for the current context: the matching tool's success rate and the
/// user's net feedback. Higher score sorts first.
/// </summary>
public class RecommendationRanker(
    IActionAnalyticsService analytics,
    IAssistantFeedbackService feedback) : IRecommendationRanker
{
    public async Task<IReadOnlyList<Recommendation>> RankAsync(
        IReadOnlyList<Recommendation> recommendations, string context)
    {
        if (recommendations.Count == 0) return recommendations;

        // Pull this context's tool metrics once and index by tool id.
        var metrics = (await analytics.GetToolMetricsAsync(context))
            .ToDictionary(m => m.ToolId, StringComparer.OrdinalIgnoreCase);

        var scored = new List<(Recommendation rec, double score)>();

        foreach (var rec in recommendations)
        {
            var score = BaseSeverityScore(rec.Severity);

            // Boost by learned success rate of the tool that would fix this, if known.
            if (metrics.TryGetValue(rec.Id, out var m) && m.TotalInvocations > 0)
                score += m.SuccessRate * 2.0;

            // Nudge by user feedback (net likes), clamped so it can't dominate severity.
            var net = await feedback.GetNetScoreAsync(rec.Id);
            score += Math.Clamp(net, -2, 2) * 0.5;

            scored.Add((rec, score));
        }

        return scored
            .OrderByDescending(s => s.score)
            .Select(s => s.rec)
            .ToList();
    }

    private static double BaseSeverityScore(FindingSeverity severity) => severity switch
    {
        FindingSeverity.Critical => 6.0,
        FindingSeverity.Warning => 4.0,
        FindingSeverity.Info => 2.0,
        _ => 1.0
    };
}
