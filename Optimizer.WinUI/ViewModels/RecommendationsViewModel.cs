using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Optimizer.WinUI.Services.Commands;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly IRecommendationsService _recommendations;
    private readonly ISmartInsightsService _insights;
    private readonly IIntelligenceService _intelligence;
    private readonly IRecommendationRanker _ranker;
    private readonly IContextDetectionService _contextDetection;
    private readonly IPageNavigator _pageNav;

    // Audit master-list (divergence): filtering must always start from the unfiltered set,
    // else All→Performance→All permanently loses the non-Performance recommendations.
    private readonly List<Recommendation> _allRecommendations = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string filterCategory = "All";
    [ObservableProperty] private bool isTrainingModel;

    public ObservableCollection<Recommendation> Recommendations { get; } = [];
    public ObservableCollection<SmartInsight> Insights { get; } = [];

    public List<string> CategoryOptions { get; } = ["All", "Performance", "Storage", "Security", "Privacy", "Stability", "Network", "Hardware", "Maintenance"];

    public string CategoryName => "Recommendations";
    public string CategoryIcon => "💡";

    public string MLStatusText => _intelligence.IsTrained
        ? $"ML model active — trained {_intelligence.LastTrainedAt:g}"
        : "ML model not yet trained";

    public bool IsEmpty => !IsLoading && Recommendations.Count == 0;

    public RecommendationsViewModel(
        IRecommendationsService recommendations,
        ISmartInsightsService insights,
        IIntelligenceService intelligence,
        IRecommendationRanker ranker,
        IContextDetectionService contextDetection,
        IPageNavigator pageNav)
    {
        _recommendations = recommendations;
        _insights = insights;
        _intelligence = intelligence;
        _ranker = ranker;
        _contextDetection = contextDetection;
        _pageNav = pageNav;
        Recommendations.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Analyzing your system...";
        try
        {
            var recs = await _recommendations.GenerateAsync();

            // Score each recommendation with the ML acceptance model
            foreach (var rec in recs)
            {
                var prob = await _intelligence.PredictAcceptanceAsync(
                    rec.Category.ToString(), rec.Severity.ToString());
                if (prob.HasValue)
                    rec.MlConfidence = prob.Value;
            }

            // Sort by ML confidence (high first); fall back to severity when untrained
            var sorted = _intelligence.IsTrained
                ? recs.OrderByDescending(r => r.MlConfidence ?? 0.5f).ToList()
                : recs.OrderByDescending(r => (int)r.Severity).ToList();

            // Phase 3: layer context-aware learning (tool success + feedback) on top.
            try
            {
                var context = await _contextDetection.DetectContextAsync();
                sorted = (await _ranker.RankAsync(sorted, context)).ToList();
            }
            catch
            {
                // Ranker is best-effort; keep the ML/severity order on failure.
            }

            ApplyFilter(sorted);

            StatusMessage = recs.Count == 0
                ? "Your system looks healthy — no recommendations right now."
                : $"{recs.Count} recommendation(s) found.";

            OnPropertyChanged(nameof(MLStatusText));
        }
        catch
        {
            StatusMessage = "Failed to generate recommendations.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadInsightsAsync()
    {
        try
        {
            var list = await _insights.GenerateAsync();
            Insights.Clear();
            foreach (var i in list)
                Insights.Add(i);
        }
        catch { /* Insights are best-effort */ }
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
        await LoadInsightsAsync();
    }

    [RelayCommand]
    public async Task DismissAsync(Recommendation rec)
    {
        await _recommendations.DismissAsync(rec.Id);
        Recommendations.Remove(rec);
        _allRecommendations.Remove(rec);
        StatusMessage = $"{Recommendations.Count} recommendation(s) remaining.";
    }

    [RelayCommand]
    public async Task ApplyActionAsync(Recommendation rec)
    {
        // Audit C10: the inline button is no longer a dead no-op for the ~all recommendations
        // that have no QuickAction. With an action → run it; without → take the user to the
        // category's page (same hub-aware routing the Command Center already uses).
        if (rec.QuickAction == null)
        {
            _pageNav.NavigateTo(CommandCenterPage.CategoryTag(rec.Category));
            StatusMessage = $"Opened the {rec.Category} section for: {rec.Title}";
            return;
        }

        IsLoading = true;
        try
        {
            var success = await rec.QuickAction();
            if (success)
            {
                await _recommendations.RecordAcceptedAsync(rec.Id);
                await _recommendations.DismissAsync(rec.Id);
                Recommendations.Remove(rec);
                _allRecommendations.Remove(rec);
                StatusMessage = $"Applied: {rec.Title}";
            }
            else
            {
                StatusMessage = $"Action failed for: {rec.Title}";
            }
        }
        catch
        {
            StatusMessage = $"Error applying: {rec.Title}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SnoozeAsync(string id)
    {
        await _recommendations.SnoozeAsync(id, TimeSpan.FromDays(7));
        var toRemove = Recommendations.FirstOrDefault(r => r.Id == id);
        if (toRemove != null)
        {
            Recommendations.Remove(toRemove);
            _allRecommendations.Remove(toRemove);
            StatusMessage = $"Snoozed for 7 days. {Recommendations.Count} remaining.";
        }
    }

    [RelayCommand]
    public async Task AcceptAsync(string id)
        => await _recommendations.RecordAcceptedAsync(id);

    [RelayCommand]
    public async Task ResetDismissedAsync()
    {
        await _recommendations.ResetDismissedAsync();
        await LoadAsync();
    }

    [RelayCommand]
    public async Task TrainModelAsync()
    {
        IsTrainingModel = true;
        StatusMessage = "Training ML intelligence model...";
        try
        {
            await _intelligence.TrainAsync();
            StatusMessage = _intelligence.IsTrained
                ? $"ML model trained successfully at {_intelligence.LastTrainedAt:g}."
                : "Not enough interaction data yet — continue using recommendations to build up training data.";
            OnPropertyChanged(nameof(MLStatusText));
        }
        catch
        {
            StatusMessage = "ML training failed.";
        }
        finally
        {
            IsTrainingModel = false;
        }
    }

    partial void OnFilterCategoryChanged(string value) => ApplyFilter(null);

    private void ApplyFilter(IReadOnlyList<Recommendation>? source)
    {
        // When a fresh source arrives (LoadAsync) it becomes the new master set. Filtering
        // always starts from the master, never from the already-filtered visible list.
        if (source != null)
        {
            _allRecommendations.Clear();
            _allRecommendations.AddRange(source);
        }

        IEnumerable<Recommendation> items = _allRecommendations;
        if (FilterCategory != "All" && Enum.TryParse<FindingCategory>(FilterCategory, out var cat))
            items = items.Where(r => r.Category == cat);

        Recommendations.Clear();
        foreach (var r in items)
            Recommendations.Add(r);
    }
}
