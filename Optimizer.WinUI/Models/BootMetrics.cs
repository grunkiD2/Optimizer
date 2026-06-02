namespace Optimizer.WinUI.Models;

public class BootMetrics
{
    public DateTime BootTime { get; set; }
    public TimeSpan BootDuration { get; set; }
    public TimeSpan? MainPathDuration { get; set; }
    public TimeSpan? BootPostBootDuration { get; set; }
    public TimeSpan? KernelBootTime { get; set; }
    public TimeSpan? ServicesBootTime { get; set; }

    public string BootDurationText => $"{BootDuration.TotalSeconds:F1}s";
    public string BootTimeText => BootTime.ToString("MMM d, h:mm tt");

    /// <summary>Normalised bar height (0–40) for the sparkline, capped at 120 s.</summary>
    public double BarHeight => Math.Min(40.0, BootDuration.TotalSeconds / 120.0 * 40.0);
}

public class StartupImpactInfo
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Impact { get; set; } = "Unknown";
    public TimeSpan? DelayMs { get; set; }
    public bool Enabled { get; set; }
}
