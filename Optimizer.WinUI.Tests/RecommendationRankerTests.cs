using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Analytics;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class RecommendationRankerTests
{
    private sealed class FakeAnalytics(Dictionary<string, ToolContextMetrics> byTool) : IActionAnalyticsService
    {
        public Task<List<ToolContextMetrics>> GetToolMetricsAsync(string? context = null)
            => Task.FromResult(byTool.Values.ToList());
        public Task<List<ToolContextMetrics>> GetTopToolsAsync(int count = 10) => Task.FromResult(new List<ToolContextMetrics>());
        public Task<List<ToolContextMetrics>> GetMostReliableToolsAsync(string context, int count = 5) => Task.FromResult(new List<ToolContextMetrics>());
        public Task<List<ToolContextMetrics>> GetProblematicToolsAsync(int count = 5) => Task.FromResult(new List<ToolContextMetrics>());
        public Task RecalculateMetricsAsync() => Task.CompletedTask;
    }

    private sealed class FakeFeedback(Dictionary<string, int> net) : IAssistantFeedbackService
    {
        public Task RecordFeedbackAsync(string? sessionId, string toolId, FeedbackVerdict verdict, string? comment = null) => Task.CompletedTask;
        public Task<int> GetNetScoreAsync(string toolId) => Task.FromResult(net.GetValueOrDefault(toolId, 0));
        public Task<List<AssistantFeedbackEntry>> GetRecentFeedbackAsync(int count = 50) => Task.FromResult(new List<AssistantFeedbackEntry>());
    }

    private static Recommendation Rec(string id, FindingSeverity sev) =>
        new() { Id = id, Title = id, Severity = sev };

    [Fact]
    public async Task Higher_severity_outranks_lower_when_no_learning_signal()
    {
        var ranker = new RecommendationRanker(
            new FakeAnalytics(new()), new FakeFeedback(new()));

        var input = new[] { Rec("a", FindingSeverity.Info), Rec("b", FindingSeverity.Critical) };
        var ranked = await ranker.RankAsync(input, "Gaming");

        Assert.Equal("b", ranked[0].Id);
        Assert.Equal("a", ranked[1].Id);
    }

    [Fact]
    public async Task High_tool_success_boosts_a_warning_above_a_critical()
    {
        // "warn" is a Warning (base 4.0) but its tool succeeds 100% (+2.0) => 6.0,
        // tying/exceeding the Critical (6.0). Add positive feedback to break ahead.
        var metrics = new Dictionary<string, ToolContextMetrics>
        {
            ["warn"] = new() { ToolId = "warn", Context = "Gaming", TotalInvocations = 10, SuccessfulInvocations = 10 }
        };
        var ranker = new RecommendationRanker(
            new FakeAnalytics(metrics), new FakeFeedback(new() { ["warn"] = 2 }));

        var input = new[] { Rec("crit", FindingSeverity.Critical), Rec("warn", FindingSeverity.Warning) };
        var ranked = await ranker.RankAsync(input, "Gaming");

        Assert.Equal("warn", ranked[0].Id);
    }

    [Fact]
    public async Task Empty_input_returns_empty()
    {
        var ranker = new RecommendationRanker(new FakeAnalytics(new()), new FakeFeedback(new()));
        var ranked = await ranker.RankAsync(System.Array.Empty<Recommendation>(), "Work");
        Assert.Empty(ranked);
    }
}
