using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Evaluates <see cref="IPredictiveMaintenanceService"/> forecasts and raises persisted,
/// deduplicated alerts when a drive is projected to fill soon or a disk is at risk.
/// </summary>
public class PredictiveAlertService(
    DatabaseService db,
    IPredictiveMaintenanceService maintenance,
    IUrgentAlertEgress? urgentEgress = null) : IPredictiveAlertService
{
    // Warn when a drive is projected to fill within this horizon.
    private const int DriveFullWarnDays = 30;

    public async Task<List<MaintenanceAlert>> EvaluateAsync()
    {
        var newAlerts = new List<MaintenanceAlert>();

        // Drive-space forecasts.
        try
        {
            foreach (var f in await maintenance.ForecastDriveSpaceAsync())
            {
                if (f.DaysUntilFull is { } days && days <= DriveFullWarnDays)
                {
                    var severity = days <= 7 ? "Critical" : "Warning";
                    var alert = new MaintenanceAlert
                    {
                        Signature = $"DiskSpace:{f.Drive}:{days / 7}", // bucket by week to limit churn
                        Kind = "DiskSpace",
                        Target = f.Drive,
                        Message = $"Drive {f.Drive} is projected to fill in ~{days} days ({f.GbPerDay:F1} GB/day).",
                        Severity = severity
                    };
                    if (await TryInsertAsync(alert)) newAlerts.Add(alert);
                }
            }
        }
        catch (Exception ex) { EngineLog.Error("Drive-space alert eval failed", ex); }

        // Disk-failure forecasts.
        try
        {
            foreach (var f in await maintenance.ForecastDiskHealthAsync())
            {
                if (f.AtRisk)
                {
                    var alert = new MaintenanceAlert
                    {
                        Signature = $"DiskFailure:{f.Serial}",
                        Kind = "DiskFailure",
                        Target = f.Model,
                        Message = $"Disk {f.Model} ({f.Serial}) is at risk: {f.Reason}." +
                                  (f.EstimatedDaysRemaining is { } d ? $" ~{d} days estimated." : ""),
                        Severity = "Critical"
                    };
                    if (await TryInsertAsync(alert)) newAlerts.Add(alert);
                }
            }
        }
        catch (Exception ex) { EngineLog.Error("Disk-failure alert eval failed", ex); }

        if (newAlerts.Count > 0)
            EngineLog.Write($"Raised {newAlerts.Count} predictive maintenance alert(s)");

        // R5 alarm-egress: a Critical maintenance forecast ("disk dying — back up NOW") must
        // reach the phone via the federation's ntfy channel, not sit in SQLite until the user
        // happens to open the dashboard. UI/event behavior above is unchanged.
        if (urgentEgress != null)
        {
            foreach (var a in newAlerts.Where(a => a.Severity == "Critical"))
            {
                try { await urgentEgress.PushUrgentAsync($"Optimizer: {a.Kind}", a.Message); }
                catch (Exception ex) { EngineLog.Error("Urgent egress push failed", ex); }
            }
        }

        return newAlerts;
    }

    public async Task<List<MaintenanceAlert>> GetActiveAlertsAsync()
    {
        const string sql = """
            SELECT Id, Signature, Kind, Target, Message, Severity, CreatedAtUtc, Acknowledged
            FROM MaintenanceAlerts
            WHERE Acknowledged = 0
            ORDER BY CreatedAtUtc DESC
            """;

        var rows = await db.ExecuteQueryAsync(sql);
        return rows.Select(Map).ToList();
    }

    public async Task AcknowledgeAsync(long alertId)
    {
        await db.ExecuteNonQueryAsync(
            "UPDATE MaintenanceAlerts SET Acknowledged = 1 WHERE Id = @id",
            new Dictionary<string, object> { ["id"] = alertId });
    }

    /// <summary>Insert an alert unless its signature already exists. Returns true if inserted.</summary>
    private async Task<bool> TryInsertAsync(MaintenanceAlert alert)
    {
        const string existsSql = "SELECT COUNT(*) FROM MaintenanceAlerts WHERE Signature = @sig";
        var count = await db.ExecuteScalarAsync<long>(existsSql,
            new Dictionary<string, object> { ["sig"] = alert.Signature });
        if (count > 0) return false;

        const string insertSql = """
            INSERT INTO MaintenanceAlerts
                (Signature, Kind, Target, Message, Severity, CreatedAtUtc, Acknowledged)
            VALUES (@sig, @kind, @target, @message, @severity, @now, 0)
            """;

        await db.ExecuteNonQueryAsync(insertSql, new Dictionary<string, object>
        {
            ["sig"] = alert.Signature,
            ["kind"] = alert.Kind,
            ["target"] = alert.Target,
            ["message"] = alert.Message,
            ["severity"] = alert.Severity,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
        return true;
    }

    private static MaintenanceAlert Map(DbRow row) => new()
    {
        Id = row.GetLong("Id"),
        Signature = row.GetString("Signature"),
        Kind = row.GetString("Kind"),
        Target = row.GetString("Target"),
        Message = row.GetString("Message"),
        Severity = row.GetString("Severity"),
        CreatedAtUtc = row.GetDateTime("CreatedAtUtc"),
        Acknowledged = row.GetBool("Acknowledged")
    };
}
