namespace Optimizer.WinUI.Services;

/// <summary>
/// Forecasts disk-space exhaustion (linear regression over daily free-space history)
/// and disk-failure risk (SMART status + wear-trend projection).
/// All computation is on-device — no data leaves the machine.
/// </summary>
public class PredictiveMaintenanceService : IPredictiveMaintenanceService
{
    private const int MinSamplesForForecast = 3;
    // Flag wear trending toward end-of-life when projected days remaining is under this threshold
    private const int WearDangerThresholdDays = 365;

    private readonly ITrendHistoryService _trendHistory;
    private readonly IDiskHealthService   _diskHealth;

    public PredictiveMaintenanceService(
        ITrendHistoryService trendHistory,
        IDiskHealthService   diskHealth)
    {
        _trendHistory = trendHistory;
        _diskHealth   = diskHealth;
    }

    // ── Drive-space forecast ──────────────────────────────────────────────────

    public Task<IReadOnlyList<DriveSpaceForecast>> ForecastDriveSpaceAsync()
    {
        var result = new List<DriveSpaceForecast>();

        foreach (var drive in DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var letter    = drive.Name.TrimEnd('\\', ':').ToUpperInvariant();
            var freeBytes = drive.AvailableFreeSpace;
            var totalBytes = drive.TotalSize;
            if (totalBytes <= 0) continue;

            var freeGb    = freeBytes  / 1_073_741_824.0;
            var totalGb   = totalBytes / 1_073_741_824.0;
            var usedPct   = 100.0 * (totalBytes - freeBytes) / totalBytes;

            var history   = _trendHistory.GetDriveFreeHistory(letter);
            var forecast  = ComputeDriveSpaceForecast(letter, freeGb, totalGb, usedPct, history);
            result.Add(forecast);
        }

        return Task.FromResult<IReadOnlyList<DriveSpaceForecast>>(result);
    }

    internal static DriveSpaceForecast ComputeDriveSpaceForecast(
        string letter,
        double currentFreeGb,
        double totalGb,
        double usedPercent,
        IReadOnlyList<(DateTime Date, long FreeBytes)> history)
    {
        if (history.Count < MinSamplesForForecast)
            return new DriveSpaceForecast(letter, currentFreeGb, totalGb, usedPercent, null, 0);

        // Build (day-offset, freeGb) pairs relative to first sample
        var origin = history[0].Date;
        var points = history
            .Select(h => ((double)(h.Date - origin).TotalDays, h.FreeBytes / 1_073_741_824.0))
            .ToList();

        // slope in GB/day; negative slope = consumption
        var slope = LinearSlope(points);

        // GbPerDay is consumption rate (positive = consuming, negative = growing)
        var gbPerDay = -slope;

        int? daysUntilFull = null;
        if (gbPerDay > 0 && currentFreeGb > 0)
            daysUntilFull = (int)Math.Ceiling(currentFreeGb / gbPerDay);

        return new DriveSpaceForecast(
            Drive:         letter,
            FreeGb:        currentFreeGb,
            TotalGb:       totalGb,
            UsedPercent:   usedPercent,
            DaysUntilFull: daysUntilFull,
            GbPerDay:      Math.Round(gbPerDay, 2));
    }

    // ── Disk-health (SMART) forecast ──────────────────────────────────────────

    public async Task<IReadOnlyList<DiskFailureForecast>> ForecastDiskHealthAsync()
    {
        var result = new List<DiskFailureForecast>();

        IReadOnlyList<Models.DiskHealthInfo> disks;
        try
        {
            disks = await _diskHealth.GetDiskHealthAsync();
        }
        catch (Exception ex)
        {
            EngineLog.Error("PredictiveMaintenanceService: SMART read failed", ex);
            return result;
        }

        foreach (var disk in disks)
        {
            var forecast = BuildDiskForecast(disk);
            result.Add(forecast);
        }

        return result;
    }

    private DiskFailureForecast BuildDiskForecast(Models.DiskHealthInfo disk)
    {
        // ── Hard SMART failure signal ─────────────────────────────────────────
        if (disk.IsPredictedToFail)
        {
            return new DiskFailureForecast(
                Model:  disk.Model,
                Serial: disk.SerialNumber,
                HealthScore: disk.HealthScore,
                AtRisk: true,
                Reason: "SMART predicts imminent failure — back up immediately.",
                EstimatedDaysRemaining: null);
        }

        // ── Wear trend projection ─────────────────────────────────────────────
        var wearHistory = _trendHistory.GetDiskWearHistory(disk.SerialNumber);
        if (wearHistory.Count >= MinSamplesForForecast)
        {
            var origin = wearHistory[0].Date;
            var points = wearHistory
                .Select(w => ((double)(w.Date - origin).TotalDays, (double)w.WearPercent))
                .ToList();

            var slope = LinearSlope(points);   // wear points per day

            if (slope > 0)
            {
                // Estimate days until wear hits 100%
                var currentWear = wearHistory[^1].WearPercent;
                var remaining   = 100.0 - currentWear;
                var daysLeft    = remaining > 0 && slope > 0
                    ? (int?)Math.Ceiling(remaining / slope)
                    : null;

                if (daysLeft.HasValue && daysLeft.Value <= WearDangerThresholdDays)
                {
                    return new DiskFailureForecast(
                        Model:  disk.Model,
                        Serial: disk.SerialNumber,
                        HealthScore: disk.HealthScore,
                        AtRisk: true,
                        Reason: $"Wear trending toward end-of-life at current rate ({slope:F3} %/day).",
                        EstimatedDaysRemaining: daysLeft);
                }
            }
        }

        // ── Temperature warning ───────────────────────────────────────────────
        if (disk.TemperatureCelsius.HasValue && disk.TemperatureCelsius.Value > 60)
        {
            return new DiskFailureForecast(
                Model:  disk.Model,
                Serial: disk.SerialNumber,
                HealthScore: disk.HealthScore,
                AtRisk: true,
                Reason: $"Drive temperature is high ({disk.TemperatureCelsius}°C). Improve case airflow.",
                EstimatedDaysRemaining: null);
        }

        // ── Healthy ───────────────────────────────────────────────────────────
        return new DiskFailureForecast(
            Model:  disk.Model,
            Serial: disk.SerialNumber,
            HealthScore: disk.HealthScore,
            AtRisk: false,
            Reason: "",
            EstimatedDaysRemaining: null);
    }

    // ── Pure math helpers (internal for unit tests) ───────────────────────────

    /// <summary>
    /// Ordinary-least-squares slope for a list of (x, y) pairs.
    /// Returns 0 when there are fewer than 2 points or when variance in x is zero.
    /// </summary>
    internal static double LinearSlope(IReadOnlyList<(double x, double y)> points)
    {
        if (points.Count < 2) return 0;

        var n  = points.Count;
        var xm = points.Average(p => p.x);
        var ym = points.Average(p => p.y);

        double num = 0, den = 0;
        foreach (var (x, y) in points)
        {
            num += (x - xm) * (y - ym);
            den += (x - xm) * (x - xm);
        }

        return den == 0 ? 0 : num / den;
    }
}
