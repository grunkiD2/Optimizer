using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>SQLite-backed per-context registry baseline snapshots.</summary>
public class ContextStateManager(DatabaseService db) : IContextStateManager
{
    public async Task SaveContextBaselineAsync(
        string context, IEnumerable<(string root, string subKey, string valueName)> targets)
    {
        var json = RegistryStateSnapshot.Capture(targets);

        const string sql = """
            INSERT INTO ContextSnapshots (Context, StateJson, CapturedAtUtc)
            VALUES (@context, @json, @now)
            ON CONFLICT(Context) DO UPDATE SET
                StateJson = @json, CapturedAtUtc = @now
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["json"] = json,
            ["now"] = DateTime.UtcNow.ToString("O")
        });

        EngineLog.Write($"Saved context baseline for {context}");
    }

    public async Task<bool> RestoreContextBaselineAsync(string context)
    {
        const string sql = "SELECT StateJson FROM ContextSnapshots WHERE Context = @context";
        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object> { ["context"] = context });
        if (rows.Count == 0) return false;

        var json = rows[0]["StateJson"]?.ToString();
        if (string.IsNullOrEmpty(json)) return false;

        try
        {
            RegistryStateSnapshot.Restore(json);
            EngineLog.Write($"Restored context baseline for {context}");
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Context baseline restore failed for {context}", ex);
            return false;
        }
    }

    public async Task<bool> HasBaselineAsync(string context)
    {
        var count = await db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM ContextSnapshots WHERE Context = @context",
            new Dictionary<string, object> { ["context"] = context });
        return count > 0;
    }
}
