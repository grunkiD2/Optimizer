using Microsoft.Extensions.Hosting;

namespace Optimizer.WinUI.Services.Data;

/// <summary>
/// R6: SQLite retention. The learning database previously grew without a single DELETE
/// (~60k rows/day across telemetry + power snapshots) — this sweeps time-keyed log tables
/// daily and VACUUMs after large prunes. Policy (docs/MACHINE-OWNERSHIP.md / merge plan):
/// raw snapshots 14 d · event logs 90 d · Fancontrol telemetry copy 30 d (the engine's
/// JSONL archive remains the source of truth — aggressive pruning of the COPY is safe).
/// Learned state (baselines, patterns, trends, profiles, wear history) is never touched.
/// </summary>
public class RetentionService(DatabaseService db) : IHostedService, IDisposable
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(3);
    private const int VacuumThreshold = 10_000;   // freed rows before a full VACUUM is worth it

    /// <summary>
    /// (table, timestamp column, max age in days). Cutoffs are DATE-ONLY strings — they
    /// compare correctly (lexicographically) against both ISO-"O" and CURRENT_TIMESTAMP
    /// ("yyyy-MM-dd HH:mm:ss") column formats, at day precision, which is all retention needs.
    /// </summary>
    private static readonly (string Table, string Column, int Days)[] Policy =
    [
        ("PowerSnapshots",      "Ts",            14),  // raw PPI attribution samples (~2.9k/day)
        ("FancontrolTelemetry", "Ts",            30),  // 5 s brain-tick COPY (~17k/day; JSONL is the archive)
        ("AssistantActions",    "ExecutedAtUtc", 90),
        ("SessionEvents",       "CreatedAtUtc",  90),
        ("History",             "TimestampUtc",  90),
        ("UndoStack",           "AppliedAtUtc",  90),  // months-old before/after snapshots are not meaningfully undoable
        ("AnomalyAlerts",       "CreatedAtUtc",  90),
        ("PowerDriftEvents",    "Ts",            90),
        ("ScheduleExecutions",  "RanAtUtc",      90),
        ("ProfileApplications", "AppliedAtUtc",  90),
    ];

    private Timer? _timer;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(async void (_) =>
        {
            try { await SweepAsync(); }
            catch (Exception ex) { EngineLog.Error("Retention sweep failed", ex); }
        }, null, StartupDelay, SweepInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>One sweep. Exposed for tests and returns total rows deleted.</summary>
    public async Task<long> SweepAsync()
    {
        long total = 0;
        foreach (var (table, column, days) in Policy)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
                total += await db.ExecuteNonQueryAsync(
                    $"DELETE FROM {table} WHERE {column} < @cutoff",
                    new Dictionary<string, object> { ["cutoff"] = cutoff });
            }
            catch (Exception ex) { EngineLog.Error($"Retention: {table} prune failed", ex); }
        }

        try
        {
            // Acknowledged maintenance alerts age out; unacknowledged ones stay until acted on.
            total += await db.ExecuteNonQueryAsync(
                "DELETE FROM MaintenanceAlerts WHERE Acknowledged = 1 AND CreatedAtUtc < @cutoff",
                new Dictionary<string, object> { ["cutoff"] = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd") });

            // Assistant sessions whose events have all aged out.
            total += await db.ExecuteNonQueryAsync(
                "DELETE FROM AssistantSessions WHERE CreatedAtUtc < @cutoff AND Id NOT IN (SELECT DISTINCT SessionId FROM SessionEvents)",
                new Dictionary<string, object> { ["cutoff"] = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd") });
        }
        catch (Exception ex) { EngineLog.Error("Retention: alert/session prune failed", ex); }

        if (total >= VacuumThreshold)
        {
            try
            {
                await db.ExecuteNonQueryAsync("VACUUM");
                EngineLog.Write($"Retention: pruned {total} rows + VACUUM");
            }
            catch (Exception ex) { EngineLog.Error("Retention: VACUUM failed", ex); }
        }
        else if (total > 0)
        {
            EngineLog.Write($"Retention: pruned {total} rows");
        }

        return total;
    }
}
