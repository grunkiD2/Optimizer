namespace Optimizer.WinUI.Services.Analytics;

/// <summary>Extracts recurring successful action sequences from the action log.</summary>
public interface IPatternExtractionService
{
    /// <summary>Scan recent actions and (re)build the learned-pattern set.</summary>
    Task ExtractPatternsAsync(int lookbackDays = 30);

    /// <summary>Get learned patterns applicable to a context (or all if null), ranked by score.</summary>
    Task<List<LearnedPattern>> GetPatternsAsync(string? context = null, int count = 10);

    /// <summary>Get the single best pattern for a context, if one clears the confidence bar.</summary>
    Task<LearnedPattern?> GetBestPatternAsync(string context);
}

/// <summary>A recurring sequence of tool actions observed to succeed in a context.</summary>
public class LearnedPattern
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Description { get; set; } = "";

    /// <summary>Ordered tool IDs forming the sequence.</summary>
    public List<string> ActionSequence { get; set; } = new();

    public string? ApplicableContext { get; set; }
    public int ObservedCount { get; set; }
    public int SuccessfulCount { get; set; }
    public double SuccessRate => ObservedCount == 0 ? 0 : SuccessfulCount / (double)ObservedCount;
    public DateTime FirstObservedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Composite confidence: success rate weighted by observation volume.</summary>
    public double Confidence => SuccessRate * Math.Min(1.0, ObservedCount / 5.0);
}
