namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Turns predictive-maintenance forecasts into deduplicated, persisted alerts so the
/// user is warned once (not every scan) about an impending disk-space or disk-failure issue.
/// </summary>
public interface IPredictiveAlertService
{
    /// <summary>Evaluate current forecasts and persist any new alerts. Returns the new ones.</summary>
    Task<List<MaintenanceAlert>> EvaluateAsync();

    /// <summary>Get unacknowledged alerts, newest first.</summary>
    Task<List<MaintenanceAlert>> GetActiveAlertsAsync();

    /// <summary>Acknowledge an alert so it stops surfacing.</summary>
    Task AcknowledgeAsync(long alertId);
}

/// <summary>A persisted predictive-maintenance alert.</summary>
public class MaintenanceAlert
{
    public long Id { get; set; }

    /// <summary>Stable identity used to dedupe repeated forecasts of the same condition.</summary>
    public string Signature { get; set; } = "";

    /// <summary>"DiskSpace" | "DiskFailure".</summary>
    public string Kind { get; set; } = "";

    public string Target { get; set; } = "";
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "Warning";
    public DateTime CreatedAtUtc { get; set; }
    public bool Acknowledged { get; set; }
}
