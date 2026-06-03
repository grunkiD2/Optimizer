using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>SQLite + settings backed implementation of the confirm-on-first-occurrence gate.</summary>
public class AutoApplyPolicy(DatabaseService db, ISettingsService settings) : IAutoApplyPolicy
{
    public async Task RecordOutcomeAsync(string optimizationId, string context, bool success)
    {
        const string sql = """
            INSERT INTO OptimizationOutcomes
                (OptimizationId, Context, SuccessCount, FailureCount, LastAppliedUtc)
            VALUES (@id, @context, @s, @f, @now)
            ON CONFLICT(OptimizationId, Context) DO UPDATE SET
                SuccessCount = SuccessCount + @s,
                FailureCount = FailureCount + @f,
                LastAppliedUtc = @now
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["id"] = optimizationId,
            ["context"] = context,
            ["s"] = success ? 1 : 0,
            ["f"] = success ? 0 : 1,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
    }

    public async Task<bool> CanAutoApplyAsync(string optimizationId, string context)
    {
        var s = settings.Settings;

        // Master gates first.
        if (s.AutomationPaused) return false;
        if (!s.AutoApplyEnabled) return false;
        if (s.AutoApplyExcluded.Contains(optimizationId, StringComparer.OrdinalIgnoreCase)) return false;

        // Confirm-on-first-occurrence: require enough prior successes in this context,
        // and no recent failures that would make auto-apply risky.
        var (successes, failures) = await GetCountsAsync(optimizationId, context);
        if (failures > 0) return false;
        return successes >= s.AutoApplySuccessThreshold;
    }

    public async Task<int> GetSuccessCountAsync(string optimizationId, string context)
    {
        var (successes, _) = await GetCountsAsync(optimizationId, context);
        return successes;
    }

    private async Task<(int successes, int failures)> GetCountsAsync(string optimizationId, string context)
    {
        const string sql = """
            SELECT SuccessCount, FailureCount FROM OptimizationOutcomes
            WHERE OptimizationId = @id AND Context = @context
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["id"] = optimizationId,
            ["context"] = context
        });

        if (rows.Count == 0) return (0, 0);
        return (Convert.ToInt32(rows[0]["SuccessCount"]), Convert.ToInt32(rows[0]["FailureCount"]));
    }
}
