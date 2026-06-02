namespace Optimizer.WinUI.Services;

public record DriveSpaceForecast(
    string Drive,
    double FreeGb,
    double TotalGb,
    double UsedPercent,
    int?   DaysUntilFull,
    double GbPerDay)
{
    /// <summary>Human-readable display for the DaysUntilFull forecast.</summary>
    public string DaysUntilFullText => DaysUntilFull.HasValue
        ? $"~{DaysUntilFull} days"
        : "Gathering data";

    public string FreeGbText   => $"{FreeGb:F1} GB";
    public string TotalGbText  => $"{TotalGb:F1} GB";
    public string GbPerDayText => $"{GbPerDay:F1} GB/day";
}

public record DiskFailureForecast(
    string Model,
    string Serial,
    int    HealthScore,
    bool   AtRisk,
    string Reason,
    int?   EstimatedDaysRemaining)
{
    public string HealthScoreText => $"{HealthScore}/100";
    public string RiskBadge       => AtRisk ? "AT RISK" : "HEALTHY";
    public string RiskColor       => AtRisk ? "#EF4444" : "#22C55E";
    public string DaysRemainingText => EstimatedDaysRemaining.HasValue
        ? $"~{EstimatedDaysRemaining} days"
        : "";
}

public interface IPredictiveMaintenanceService
{
    Task<IReadOnlyList<DriveSpaceForecast>>   ForecastDriveSpaceAsync();
    Task<IReadOnlyList<DiskFailureForecast>>  ForecastDiskHealthAsync();
}
