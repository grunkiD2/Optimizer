using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Data;

/// <summary>SQLite-backed change-set recorder with before/after registry snapshots.</summary>
public class ChangeSetService(DatabaseService db) : IChangeSetService
{
    public string CaptureBefore(IEnumerable<(string root, string subKey, string valueName)> targets)
        => RegistryStateSnapshot.Capture(targets);

    public async Task<long> CommitAsync(
        string optimizationId,
        string title,
        string beforeSnapshot,
        IEnumerable<(string root, string subKey, string valueName)> targets,
        string? groupId = null,
        string? context = null)
    {
        var afterSnapshot = RegistryStateSnapshot.Capture(targets);

        const string sql = """
            INSERT INTO UndoStack
                (OptimizationId, Title, GroupId, BeforeState, AfterState,
                 AppliedAtUtc, Reversible, IsUndone, DetectedContext)
            VALUES
                (@optId, @title, @groupId, @before, @after,
                 @appliedAt, 1, 0, @context);
            SELECT last_insert_rowid();
            """;

        var parameters = new Dictionary<string, object>
        {
            ["optId"] = optimizationId,
            ["title"] = title,
            ["groupId"] = groupId ?? "",
            ["before"] = beforeSnapshot,
            ["after"] = afterSnapshot,
            ["appliedAt"] = DateTime.UtcNow.ToString("O"),
            ["context"] = context ?? "Unknown"
        };

        var id = await db.ExecuteScalarAsync<long>(sql, parameters);
        EngineLog.Write($"Recorded change set #{id} for {optimizationId}");
        return id;
    }

    public async Task<List<ChangeSet>> GetRecentAsync(int count = 100)
    {
        const string sql = """
            SELECT Id, OptimizationId, Title, GroupId, BeforeState, AfterState,
                   AppliedAtUtc, Reversible, IsUndone, DetectedContext
            FROM UndoStack
            ORDER BY AppliedAtUtc DESC
            LIMIT @count
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object> { ["count"] = count });
        return rows.Select(Map).ToList();
    }

    public async Task<List<ChangeSet>> GetByGroupAsync(string groupId)
    {
        const string sql = """
            SELECT Id, OptimizationId, Title, GroupId, BeforeState, AfterState,
                   AppliedAtUtc, Reversible, IsUndone, DetectedContext
            FROM UndoStack
            WHERE GroupId = @groupId
            ORDER BY AppliedAtUtc ASC
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object> { ["groupId"] = groupId });
        return rows.Select(Map).ToList();
    }

    public async Task<bool> RestoreAsync(long changeSetId)
    {
        const string fetchSql = """
            SELECT BeforeState, IsUndone FROM UndoStack WHERE Id = @id
            """;
        var rows = await db.ExecuteQueryAsync(fetchSql, new Dictionary<string, object> { ["id"] = changeSetId });
        if (rows.Count == 0) return false;

        var before = rows[0]["BeforeState"]?.ToString();
        var alreadyUndone = Convert.ToInt32(rows[0]["IsUndone"]) == 1;
        if (alreadyUndone || string.IsNullOrEmpty(before)) return false;

        try
        {
            RegistryStateSnapshot.Restore(before);
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Change-set restore failed (#{changeSetId})", ex);
            return false;
        }

        await db.ExecuteNonQueryAsync(
            "UPDATE UndoStack SET IsUndone = 1 WHERE Id = @id",
            new Dictionary<string, object> { ["id"] = changeSetId });

        EngineLog.Write($"Restored change set #{changeSetId}");
        return true;
    }

    private static ChangeSet Map(DbRow row) => new()
    {
        Id = row.GetLong("Id"),
        OptimizationId = row.GetString("OptimizationId"),
        Title = row.GetString("Title"),
        GroupId = row.GetStringOrNull("GroupId"),
        BeforeState = row.GetStringOrNull("BeforeState"),
        AfterState = row.GetStringOrNull("AfterState"),
        AppliedAtUtc = row.GetDateTime("AppliedAtUtc"),
        Reversible = row.GetBool("Reversible"),
        IsUndone = row.GetBool("IsUndone"),
        Context = row.GetStringOrNull("DetectedContext")
    };
}
