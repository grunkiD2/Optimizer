using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Optimizer.WinUI.ViewModels;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class LearningDashboardTests
{
    private sealed class FakeContext : IContextDetectionService
    {
        public Task<string> DetectContextAsync() => Task.FromResult("Gaming");
        public UserIntent UserIntent => UserIntent.None;
    }

    private sealed class FakeAnalytics : IActionAnalyticsService
    {
        public Task<List<ToolContextMetrics>> GetToolMetricsAsync(string? context = null) => Task.FromResult(new List<ToolContextMetrics>());
        public Task<List<ToolContextMetrics>> GetTopToolsAsync(int count = 10) => Task.FromResult(new List<ToolContextMetrics>
        {
            new() { ToolId = "apply_profile", Context = "Gaming", TotalInvocations = 10, SuccessfulInvocations = 9 }
        });
        public Task<List<ToolContextMetrics>> GetMostReliableToolsAsync(string context, int count = 5) => Task.FromResult(new List<ToolContextMetrics>());
        public Task<List<ToolContextMetrics>> GetProblematicToolsAsync(int count = 5) => Task.FromResult(new List<ToolContextMetrics>());
        public Task RecalculateMetricsAsync() => Task.CompletedTask;
    }

    private sealed class FakePatterns : IPatternExtractionService
    {
        public Task ExtractPatternsAsync(int lookbackDays = 30) => Task.CompletedTask;
        public Task<List<LearnedPattern>> GetPatternsAsync(string? context = null, int count = 10) => Task.FromResult(new List<LearnedPattern>
        {
            new() { Description = "In Gaming: a → b", ObservedCount = 4, SuccessfulCount = 4 }
        });
        public Task<LearnedPattern?> GetBestPatternAsync(string context) => Task.FromResult<LearnedPattern?>(null);
    }

    private sealed class FakeRuleSuggestions : IRuleSuggestionService
    {
        public Task GenerateSuggestionsAsync(int lookbackDays = 30) => Task.CompletedTask;
        public Task<List<SuggestedAutomationRule>> GetPendingSuggestionsAsync() => Task.FromResult(new List<SuggestedAutomationRule>
        {
            new() { ProfileName = "Gaming", TriggerType = "TimeRange", TriggerValue = "22:00-00:00", ConfidenceScore = 0.8, ReasoningText = "nightly" }
        });
        public Task AcceptSuggestionAsync(string suggestionId) => Task.CompletedTask;
        public Task RejectSuggestionAsync(string suggestionId) => Task.CompletedTask;
    }

    private sealed class FakePredictive : IPredictiveAlertService
    {
        public Task<List<MaintenanceAlert>> EvaluateAsync() => Task.FromResult(new List<MaintenanceAlert>());
        public Task<List<MaintenanceAlert>> GetActiveAlertsAsync() => Task.FromResult(new List<MaintenanceAlert>
        {
            new() { Id = 1, Severity = "Critical", Message = "Drive C will fill in ~5 days." }
        });
        public Task AcknowledgeAsync(long alertId) => Task.CompletedTask;
    }

    private static LearningDashboardViewModel NewVm() =>
        new(new FakeContext(), new FakeAnalytics(), new FakePatterns(), new FakeRuleSuggestions(), new FakePredictive());

    [Fact]
    public async Task Load_populates_all_sections()
    {
        var vm = NewVm();
        await vm.LoadAsync();

        Assert.Equal("Gaming", vm.CurrentContext);
        Assert.Single(vm.TopTools);
        Assert.Single(vm.Patterns);
        Assert.Single(vm.Suggestions);
        Assert.Single(vm.Alerts);
        Assert.False(vm.NoAlerts);
    }

    [Fact]
    public async Task BuildReport_includes_context_and_sections()
    {
        var vm = NewVm();
        await vm.LoadAsync();

        var report = vm.BuildReport();
        Assert.Contains("Gaming", report);
        Assert.Contains("Top tools", report);
        Assert.Contains("Learned patterns", report);
        Assert.Contains("automation suggestions", report);
        Assert.Contains("maintenance alerts", report);
    }

    [Fact]
    public async Task Acknowledge_alert_removes_it()
    {
        var vm = NewVm();
        await vm.LoadAsync();
        var alert = Assert.Single(vm.Alerts);

        await vm.AcknowledgeAlertCommand.ExecuteAsync(alert);

        Assert.Empty(vm.Alerts);
        Assert.True(vm.NoAlerts);
    }
}
