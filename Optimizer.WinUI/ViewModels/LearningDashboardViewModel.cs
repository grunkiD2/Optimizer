using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;

namespace Optimizer.WinUI.ViewModels;

/// <summary>
/// Surfaces everything the learning layer has gathered: current context, the tools that
/// work best, mined patterns, pending automation suggestions, and predictive alerts.
/// </summary>
public partial class LearningDashboardViewModel : ObservableObject
{
    private readonly IContextDetectionService _contextDetection;
    private readonly IActionAnalyticsService _analytics;
    private readonly IPatternExtractionService _patterns;
    private readonly IRuleSuggestionService _ruleSuggestions;
    private readonly IPredictiveAlertService _predictive;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string currentContext = "Unknown";
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<ToolMetricRow> TopTools { get; } = [];
    public ObservableCollection<PatternRow> Patterns { get; } = [];
    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];
    public ObservableCollection<AlertRow> Alerts { get; } = [];

    public string CategoryName => "Learning";
    public string CategoryIcon => "🧠";

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    public bool NoTools => !IsLoading && TopTools.Count == 0;
    public bool NoPatterns => !IsLoading && Patterns.Count == 0;
    public bool NoSuggestions => !IsLoading && Suggestions.Count == 0;
    public bool NoAlerts => !IsLoading && Alerts.Count == 0;

    public LearningDashboardViewModel(
        IContextDetectionService contextDetection,
        IActionAnalyticsService analytics,
        IPatternExtractionService patterns,
        IRuleSuggestionService ruleSuggestions,
        IPredictiveAlertService predictive)
    {
        _contextDetection = contextDetection;
        _analytics = analytics;
        _patterns = patterns;
        _ruleSuggestions = ruleSuggestions;
        _predictive = predictive;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Gathering what Optimizer has learned…";
        RaiseEmptyStates();

        try
        {
            CurrentContext = await _contextDetection.DetectContextAsync();

            TopTools.Clear();
            foreach (var m in await _analytics.GetTopToolsAsync(8))
                TopTools.Add(new ToolMetricRow(
                    m.ToolId, m.Context, $"{m.SuccessRate * 100:F0}%", m.TotalInvocations));

            Patterns.Clear();
            foreach (var p in await _patterns.GetPatternsAsync(count: 8))
                Patterns.Add(new PatternRow(
                    p.Description, $"{p.SuccessRate * 100:F0}%", p.ObservedCount));

            Suggestions.Clear();
            foreach (var s in await _ruleSuggestions.GetPendingSuggestionsAsync())
                Suggestions.Add(new SuggestionRow(
                    s.Id, s.ProfileName, $"{s.TriggerType} {s.TriggerValue}",
                    s.ReasoningText, $"{s.ConfidenceScore * 100:F0}%"));

            Alerts.Clear();
            foreach (var a in await _predictive.GetActiveAlertsAsync())
                Alerts.Add(new AlertRow(a.Id, a.Severity, a.Message));

            StatusMessage = $"Context: {CurrentContext}. " +
                            $"{TopTools.Count} tools, {Patterns.Count} patterns, " +
                            $"{Suggestions.Count} suggestions, {Alerts.Count} alerts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load learning data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            RaiseEmptyStates();
        }
    }

    [RelayCommand]
    public async Task AcceptSuggestionAsync(SuggestionRow row)
    {
        if (row is null) return;
        await _ruleSuggestions.AcceptSuggestionAsync(row.Id);
        Suggestions.Remove(row);
        OnPropertyChanged(nameof(NoSuggestions));
        StatusMessage = $"Accepted suggestion for {row.ProfileName}.";
    }

    [RelayCommand]
    public async Task RejectSuggestionAsync(SuggestionRow row)
    {
        if (row is null) return;
        await _ruleSuggestions.RejectSuggestionAsync(row.Id);
        Suggestions.Remove(row);
        OnPropertyChanged(nameof(NoSuggestions));
        StatusMessage = $"Dismissed suggestion for {row.ProfileName}.";
    }

    [RelayCommand]
    public async Task AcknowledgeAlertAsync(AlertRow row)
    {
        if (row is null) return;
        await _predictive.AcknowledgeAsync(row.Id);
        Alerts.Remove(row);
        OnPropertyChanged(nameof(NoAlerts));
    }

    /// <summary>Build a human-readable markdown report of the current learning state.</summary>
    public string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Optimizer Learning Report");
        sb.AppendLine($"_Generated {DateTime.Now:yyyy-MM-dd HH:mm}_  ");
        sb.AppendLine($"Current context: **{CurrentContext}**");
        sb.AppendLine();

        sb.AppendLine("## Top tools");
        foreach (var t in TopTools)
            sb.AppendLine($"- `{t.ToolId}` ({t.Context}) — {t.SuccessRate} success over {t.Invocations}");
        sb.AppendLine();

        sb.AppendLine("## Learned patterns");
        foreach (var p in Patterns)
            sb.AppendLine($"- {p.Description} — {p.SuccessRate} over {p.ObservedCount}");
        sb.AppendLine();

        sb.AppendLine("## Pending automation suggestions");
        foreach (var s in Suggestions)
            sb.AppendLine($"- {s.ProfileName}: {s.Trigger} ({s.Confidence}) — {s.Reasoning}");
        sb.AppendLine();

        sb.AppendLine("## Active maintenance alerts");
        foreach (var a in Alerts)
            sb.AppendLine($"- [{a.Severity}] {a.Message}");

        return sb.ToString();
    }

    private void RaiseEmptyStates()
    {
        OnPropertyChanged(nameof(HasStatusMessage));
        OnPropertyChanged(nameof(NoTools));
        OnPropertyChanged(nameof(NoPatterns));
        OnPropertyChanged(nameof(NoSuggestions));
        OnPropertyChanged(nameof(NoAlerts));
    }
}

// ── Display rows ────────────────────────────────────────────────────────────

public record ToolMetricRow(string ToolId, string Context, string SuccessRate, int Invocations);
public record PatternRow(string Description, string SuccessRate, int ObservedCount);
public record SuggestionRow(string Id, string ProfileName, string Trigger, string Reasoning, string Confidence);
public record AlertRow(long Id, string Severity, string Message);
