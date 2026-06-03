namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Observes profile-application history and proposes automation rules
/// (e.g. "you apply Gaming nightly around 22:00 — automate it?").
/// </summary>
public interface IRuleSuggestionService
{
    /// <summary>Analyze history and (re)generate pending suggestions.</summary>
    Task GenerateSuggestionsAsync(int lookbackDays = 30);

    /// <summary>Get current pending suggestions, highest-confidence first.</summary>
    Task<List<SuggestedAutomationRule>> GetPendingSuggestionsAsync();

    /// <summary>Mark a suggestion accepted (caller is responsible for creating the real rule).</summary>
    Task AcceptSuggestionAsync(string suggestionId);

    /// <summary>Mark a suggestion rejected so it isn't surfaced again.</summary>
    Task RejectSuggestionAsync(string suggestionId);
}

/// <summary>A proposed automation rule derived from observed behavior.</summary>
public class SuggestedAutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";

    /// <summary>"TimeRange" or "ProcessRunning".</summary>
    public string TriggerType { get; set; } = "TimeRange";

    /// <summary>e.g. "21:00-23:00" for TimeRange, or a process name for ProcessRunning.</summary>
    public string TriggerValue { get; set; } = "";

    public double ConfidenceScore { get; set; }
    public string ReasoningText { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
