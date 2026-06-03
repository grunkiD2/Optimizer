namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Logs assistant tool invocations for learning and analytics.</summary>
public interface IAssistantActionLogger
{
    /// <summary>Log a tool execution with outcome.</summary>
    Task LogActionAsync(
        string toolId,
        string? arguments,
        bool success,
        string? errorMessage = null,
        int executionTimeMs = 0,
        string? detectedContext = null);

    /// <summary>Get action metrics for a tool in a specific context.</summary>
    Task<ToolActionMetrics?> GetMetricsAsync(string toolId, string? context = null);

    /// <summary>Get recent actions (default last 30 days).</summary>
    Task<List<AssistantActionLog>> GetRecentActionsAsync(int dayCount = 30);
}

/// <summary>Represents a logged assistant action.</summary>
public class AssistantActionLog
{
    public int Id { get; set; }
    public string ToolId { get; set; } = "";
    public string? Arguments { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
    public int ExecutionTimeMs { get; set; }
    public string? DetectedContext { get; set; }
}

/// <summary>Aggregated metrics for a tool in a context.</summary>
public class ToolActionMetrics
{
    public string ToolId { get; set; } = "";
    public string? Context { get; set; }
    public int TotalInvocations { get; set; }
    public int SuccessfulInvocations { get; set; }
    public double SuccessRate => TotalInvocations == 0 ? 0 : SuccessfulInvocations / (double)TotalInvocations;
    public double AverageDurationMs { get; set; }
    public DateTime? LastInvokedUtc { get; set; }
}
