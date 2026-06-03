using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services;

/// <summary>
/// SQLite-backed scheduler. A hosted background loop wakes every minute and applies any
/// tasks whose <c>NextRunUtc</c> has passed, then recomputes their next occurrence.
/// </summary>
public class ScheduledOptimizationService(
    DatabaseService db,
    IWindowsOptimizerService optimizer) : IScheduledOptimizationService, IHostedService, IDisposable
{
    private Timer? _timer;

    // ── Lifecycle ───────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // First tick after 60s, then every 60s.
        _timer = new Timer(async void (_) =>
        {
            try { await EvaluateDueAsync(); }
            catch (Exception ex) { EngineLog.Error("Scheduler tick failed", ex); }
        }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
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

    // ── CRUD ────────────────────────────────────────────────────────────────

    public async Task<ScheduledTask> CreateAsync(ScheduledTask task)
    {
        task.NextRunUtc = ComputeNextRun(task, DateTime.UtcNow);

        const string sql = """
            INSERT INTO ScheduledTasks
                (Id, Kind, TargetId, ScheduleType, ScheduleValue, Enabled,
                 LastRunUtc, NextRunUtc, CreatedAtUtc)
            VALUES
                (@id, @kind, @targetId, @type, @value, @enabled,
                 @lastRun, @nextRun, @createdAt)
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["id"] = task.Id,
            ["kind"] = task.Kind,
            ["targetId"] = task.TargetId,
            ["type"] = task.ScheduleType,
            ["value"] = task.ScheduleValue,
            ["enabled"] = task.Enabled ? 1 : 0,
            ["lastRun"] = task.LastRunUtc?.ToString("O") ?? "",
            ["nextRun"] = task.NextRunUtc?.ToString("O") ?? "",
            ["createdAt"] = task.CreatedAtUtc.ToString("O")
        });

        EngineLog.Write($"Scheduled {task.Kind} '{task.TargetId}' ({task.ScheduleType} {task.ScheduleValue})");
        return task;
    }

    public async Task<List<ScheduledTask>> GetAllAsync()
    {
        const string sql = """
            SELECT Id, Kind, TargetId, ScheduleType, ScheduleValue, Enabled,
                   LastRunUtc, NextRunUtc, CreatedAtUtc
            FROM ScheduledTasks
            ORDER BY NextRunUtc ASC
            """;

        var rows = await db.ExecuteQueryAsync(sql);
        return rows.Select(Map).ToList();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var n = await db.ExecuteNonQueryAsync(
            "DELETE FROM ScheduledTasks WHERE Id = @id",
            new Dictionary<string, object> { ["id"] = id });
        return n > 0;
    }

    public async Task<bool> SetEnabledAsync(string id, bool enabled)
    {
        var n = await db.ExecuteNonQueryAsync(
            "UPDATE ScheduledTasks SET Enabled = @e WHERE Id = @id",
            new Dictionary<string, object> { ["e"] = enabled ? 1 : 0, ["id"] = id });
        return n > 0;
    }

    // ── Evaluation ────────────────────────────────────────────────────────────

    public async Task EvaluateDueAsync()
    {
        var now = DateTime.UtcNow;

        const string dueSql = """
            SELECT Id, Kind, TargetId, ScheduleType, ScheduleValue, Enabled,
                   LastRunUtc, NextRunUtc, CreatedAtUtc
            FROM ScheduledTasks
            WHERE Enabled = 1 AND NextRunUtc IS NOT NULL AND NextRunUtc != '' AND NextRunUtc <= @now
            """;

        var due = (await db.ExecuteQueryAsync(dueSql,
            new Dictionary<string, object> { ["now"] = now.ToString("O") }))
            .Select(Map).ToList();

        foreach (var task in due)
        {
            bool success;
            string message;
            try
            {
                if (task.Kind == "optimization")
                {
                    var r = await optimizer.ApplyOptimizationAsync(task.TargetId);
                    success = r.Success;
                    message = r.Message;
                }
                else
                {
                    success = await optimizer.ApplyProfileAsync(task.TargetId);
                    message = success ? "Applied" : "Completed with errors";
                }
            }
            catch (Exception ex)
            {
                success = false;
                message = ex.Message;
            }

            await LogExecutionAsync(task.Id, success, message);

            // Compute the next occurrence (or disable a one-shot).
            var next = task.ScheduleType == "Once" ? (DateTime?)null : ComputeNextRun(task, now.AddMinutes(1));
            await db.ExecuteNonQueryAsync("""
                UPDATE ScheduledTasks
                SET LastRunUtc = @lastRun,
                    NextRunUtc = @nextRun,
                    Enabled = @enabled
                WHERE Id = @id
                """, new Dictionary<string, object>
            {
                ["lastRun"] = now.ToString("O"),
                ["nextRun"] = next?.ToString("O") ?? "",
                ["enabled"] = task.ScheduleType == "Once" ? 0 : 1,
                ["id"] = task.Id
            });

            EngineLog.Write($"Scheduled task ran: {task.Kind} '{task.TargetId}' → {(success ? "ok" : "failed")}");
        }
    }

    private async Task LogExecutionAsync(string taskId, bool success, string message)
    {
        await db.ExecuteNonQueryAsync("""
            INSERT INTO ScheduleExecutions (TaskId, RanAtUtc, Success, Message)
            VALUES (@taskId, @ranAt, @success, @message)
            """, new Dictionary<string, object>
        {
            ["taskId"] = taskId,
            ["ranAt"] = DateTime.UtcNow.ToString("O"),
            ["success"] = success ? 1 : 0,
            ["message"] = message
        });
    }

    /// <summary>Compute the next run time at or after <paramref name="from"/>.</summary>
    internal static DateTime? ComputeNextRun(ScheduledTask task, DateTime from)
    {
        switch (task.ScheduleType)
        {
            case "DailyAt":
                // ScheduleValue is local "HH:mm".
                if (!TimeSpan.TryParse(task.ScheduleValue, out var tod)) return null;
                var localFrom = from.ToLocalTime();
                var candidate = localFrom.Date + tod;
                if (candidate <= localFrom) candidate = candidate.AddDays(1);
                return candidate.ToUniversalTime();

            case "IntervalMinutes":
                if (!int.TryParse(task.ScheduleValue, out var minutes) || minutes <= 0) return null;
                return from.AddMinutes(minutes);

            case "Once":
                if (!DateTime.TryParse(task.ScheduleValue, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var when)) return null;
                var whenUtc = when.ToUniversalTime();
                return whenUtc <= from ? null : whenUtc;

            default:
                return null;
        }
    }

    private static ScheduledTask Map(Dictionary<string, object> row) => new()
    {
        Id = row["Id"].ToString()!,
        Kind = row["Kind"].ToString()!,
        TargetId = row["TargetId"].ToString()!,
        ScheduleType = row["ScheduleType"].ToString()!,
        ScheduleValue = row["ScheduleValue"].ToString()!,
        Enabled = Convert.ToInt32(row["Enabled"]) == 1,
        LastRunUtc = ParseNullable(row["LastRunUtc"]),
        NextRunUtc = ParseNullable(row["NextRunUtc"]),
        CreatedAtUtc = DateTime.Parse(row["CreatedAtUtc"].ToString()!)
    };

    private static DateTime? ParseNullable(object? value)
    {
        var s = value?.ToString();
        return string.IsNullOrEmpty(s) ? null : DateTime.Parse(s);
    }
}
