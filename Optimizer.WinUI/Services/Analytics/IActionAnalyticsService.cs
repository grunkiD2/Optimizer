namespace Optimizer.WinUI.Services.Analytics;

/// <summary>Analyzes tool invocation patterns and success rates across contexts.</summary>
public interface IActionAnalyticsService
{
    /// <summary>Get metrics for a specific tool in a context (or all contexts if context is null).</summary>
    Task<List<ToolContextMetrics>> GetToolMetricsAsync(string? context = null);

    /// <summary>Get top tools by invocation count.</summary>
    Task<List<ToolContextMetrics>> GetTopToolsAsync(int count = 10);

    /// <summary>Get tools with highest success rate in a context.</summary>
    Task<List<ToolContextMetrics>> GetMostReliableToolsAsync(string context, int count = 5);

    /// <summary>Get tools with low success rate (candidates for review).</summary>
    Task<List<ToolContextMetrics>> GetProblematicToolsAsync(int count = 5);

    /// <summary>Recalculate all metrics from action log (expensive operation).</summary>
    Task RecalculateMetricsAsync();
}

/// <summary>Metrics for a tool in a specific context.</summary>
public class ToolContextMetrics
{
    public string ToolId { get; set; } = "";
    public string Context { get; set; } = "";
    public int TotalInvocations { get; set; }
    public int SuccessfulInvocations { get; set; }
    public double SuccessRate => TotalInvocations == 0 ? 0 : SuccessfulInvocations / (double)TotalInvocations;
    public double AverageDurationMs { get; set; }
    public DateTime? LastInvokedUtc { get; set; }
}
