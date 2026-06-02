using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class RecommendationPreference
{
    public string Id { get; set; } = "";
    public int AcceptCount { get; set; }
    public int DismissCount { get; set; }
    public DateTime? SnoozedUntilUtc { get; set; }
    public DateTime LastShownUtc { get; set; }
}

public interface IRecommendationsService
{
    Task<IReadOnlyList<Recommendation>> GenerateAsync();
    Task DismissAsync(string id);
    Task ResetDismissedAsync();
    Task RecordAcceptedAsync(string id);
    Task SnoozeAsync(string id, TimeSpan duration);
    IReadOnlyDictionary<string, RecommendationPreference> GetPreferences();
}
