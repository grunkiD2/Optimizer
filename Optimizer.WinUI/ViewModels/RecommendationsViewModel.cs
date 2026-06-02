using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly IRecommendationsService _recommendations;
    private readonly ISmartInsightsService _insights;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string filterCategory = "All";

    public ObservableCollection<Recommendation> Recommendations { get; } = [];
    public ObservableCollection<SmartInsight> Insights { get; } = [];

    public List<string> CategoryOptions { get; } = ["All", "Performance", "Storage", "Security", "Privacy", "Stability", "Network", "Hardware", "Maintenance"];

    public string CategoryName => "Recommendations";
    public string CategoryIcon => "💡";

    public RecommendationsViewModel(IRecommendationsService recommendations, ISmartInsightsService insights)
    {
        _recommendations = recommendations;
        _insights = insights;
    }

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Analyzing your system...";
        try
        {
            var recs = await _recommendations.GenerateAsync();
            ApplyFilter(recs);
            StatusMessage = recs.Count == 0
                ? "Your system looks healthy — no recommendations right now."
                : $"{recs.Count} recommendation(s) found.";
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
        StatusMessage = $"{Recommendations.Count} recommendation(s) remaining.";
    }

    [RelayCommand]
    public async Task ApplyActionAsync(Recommendation rec)
    {
        if (rec.QuickAction == null) return;
        IsLoading = true;
        try
        {
            var success = await rec.QuickAction();
            if (success)
            {
                await _recommendations.RecordAcceptedAsync(rec.Id);
                await _recommendations.DismissAsync(rec.Id);
                Recommendations.Remove(rec);
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

    partial void OnFilterCategoryChanged(string value) => ApplyFilter(null);

    private void ApplyFilter(IReadOnlyList<Recommendation>? source)
    {
        IEnumerable<Recommendation> items = source ?? Recommendations.ToList();

        if (FilterCategory != "All" && Enum.TryParse<FindingCategory>(FilterCategory, out var cat))
            items = items.Where(r => r.Category == cat);

        Recommendations.Clear();
        foreach (var r in items.OrderByDescending(r => (int)r.Severity))
            Recommendations.Add(r);
    }
}
