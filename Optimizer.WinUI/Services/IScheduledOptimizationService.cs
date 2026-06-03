namespace Optimizer.WinUI.Services;

/// <summary>
/// Schedules profiles/optimizations to run unattended on a daily-time, fixed-interval,
/// or one-shot basis. Evaluated once a minute by a background loop.
/// </summary>
public interface IScheduledOptimizationService
{
    Task<ScheduledTask> CreateAsync(ScheduledTask task);
    Task<List<ScheduledTask>> GetAllAsync();
    Task<bool> DeleteAsync(string id);
    Task<bool> SetEnabledAsync(string id, bool enabled);

    /// <summary>Run any tasks that are due now. Called by the background loop.</summary>
    Task EvaluateDueAsync();
}

/// <summary>A scheduled optimization or profile application.</summary>
public class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>"profile" or "optimization".</summary>
    public string Kind { get; set; } = "profile";

    /// <summary>Profile id or optimization id to apply.</summary>
    public string TargetId { get; set; } = "";

    /// <summary>"DailyAt" | "IntervalMinutes" | "Once".</summary>
    public string ScheduleType { get; set; } = "DailyAt";

    /// <summary>"HH:mm" for DailyAt, an integer for IntervalMinutes, ISO-8601 for Once.</summary>
    public string ScheduleValue { get; set; } = "03:00";

    public bool Enabled { get; set; } = true;
    public DateTime? LastRunUtc { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
