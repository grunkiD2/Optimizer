using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.Mobile.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Optimizer.Mobile.ViewModels;

public partial class RecommendationModel : ObservableObject
{
    public string Title       { get; set; } = "";
    public string Description { get; set; } = "";
    public string Severity    { get; set; } = "info";

    public Color SeverityColor => Severity.ToLowerInvariant() switch
    {
        "critical" => Color.FromArgb("#EF4444"),
        "warning"  => Color.FromArgb("#F59E0B"),
        _          => Color.FromArgb("#3B82F6")
    };
}

public partial class RecommendationsViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<RecommendationModel> Recommendations { get; } = new();

    public RecommendationsViewModel(ApiClient api)
    {
        _api = api;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        HasError = false;
        try
        {
            var data = await _api.GetJsonAsync("/api/recommendations");
            Recommendations.Clear();
            if (data.HasValue && data.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in data.Value.EnumerateArray())
                {
                    Recommendations.Add(new RecommendationModel
                    {
                        Title       = r.TryGetProperty("title",       out var t) ? t.GetString() ?? "" : "",
                        Description = r.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                        Severity    = r.TryGetProperty("severity",    out var s) ? s.GetString() ?? "info" : "info"
                    });
                }
            }
            StatusMessage = Recommendations.Count == 0 ? "No recommendations — your system looks great!" : "";
        }
        catch
        {
            HasError = true;
            StatusMessage = "Could not load recommendations.";
        }
        finally { IsBusy = false; }
    }
}
