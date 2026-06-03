using System.Text.Json;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Persists assistant conversation sessions to SQLite.</summary>
public class SessionPersistence(DatabaseService db) : ISessionPersistence
{
    private const string DateFormat = "yyyy-MM-dd";

    public async Task<AssistantSession> GetOrCreateTodaySessionAsync()
    {
        var today = DateTime.UtcNow.Date;
        var dateStr = today.ToString(DateFormat);

        const string checkSql = "SELECT Id FROM AssistantSessions WHERE SessionDate = @date AND ArchivedAtUtc IS NULL LIMIT 1";
        var parameters = new Dictionary<string, object> { ["date"] = dateStr };

        var existing = await db.ExecuteQueryAsync(checkSql, parameters);
        if (existing.Count > 0)
        {
            var id = existing[0]["Id"].ToString()!;
            return new AssistantSession
            {
                Id = id,
                SessionDate = today,
                CreatedAtUtc = DateTime.UtcNow,
                ArchivedAtUtc = null
            };
        }

        // Create new session
        var sessionId = Guid.NewGuid().ToString();
        const string insertSql = """
            INSERT INTO AssistantSessions (Id, SessionDate, CreatedAtUtc)
            VALUES (@id, @date, @createdAt)
            """;

        var insertParams = new Dictionary<string, object>
        {
            ["id"] = sessionId,
            ["date"] = dateStr,
            ["createdAt"] = DateTime.UtcNow.ToString("O")
        };

        await db.ExecuteNonQueryAsync(insertSql, insertParams);
        return new AssistantSession
        {
            Id = sessionId,
            SessionDate = today,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public async Task<List<SessionEvent>> LoadSessionEventsAsync(string sessionId)
    {
        const string sql = """
            SELECT Id, SessionId, EventType, Content, CreatedAtUtc
            FROM SessionEvents
            WHERE SessionId = @sessionId
            ORDER BY CreatedAtUtc ASC
            """;

        var parameters = new Dictionary<string, object> { ["sessionId"] = sessionId };
        var results = await db.ExecuteQueryAsync(sql, parameters);

        return results.Select(row => new SessionEvent
        {
            Id = Convert.ToInt32(row["Id"]),
            SessionId = row["SessionId"].ToString()!,
            EventType = Enum.Parse<SessionEventType>(row["EventType"].ToString()!),
            Content = row["Content"]?.ToString() ?? "",
            CreatedAtUtc = DateTime.Parse(row["CreatedAtUtc"].ToString()!)
        }).ToList();
    }

    public async Task AppendEventAsync(string sessionId, SessionEventType eventType, string content)
    {
        const string sql = """
            INSERT INTO SessionEvents (SessionId, EventType, Content, CreatedAtUtc)
            VALUES (@sessionId, @eventType, @content, @createdAt)
            """;

        var parameters = new Dictionary<string, object>
        {
            ["sessionId"] = sessionId,
            ["eventType"] = eventType.ToString(),
            ["content"] = content,
            ["createdAt"] = DateTime.UtcNow.ToString("O")
        };

        await db.ExecuteNonQueryAsync(sql, parameters);
    }

    public async Task<List<AssistantSession>> GetSessionsAsync(DateTime? since = null)
    {
        var whereSql = since.HasValue ? "WHERE CreatedAtUtc >= @since" : "";
        var parameters = new Dictionary<string, object>();

        if (since.HasValue)
            parameters["since"] = since.Value.ToString("O");

        var sql = $"""
            SELECT Id, SessionDate, CreatedAtUtc, ArchivedAtUtc
            FROM AssistantSessions
            {whereSql}
            ORDER BY SessionDate DESC
            LIMIT 100
            """;

        var results = await db.ExecuteQueryAsync(sql, parameters);
        return results.Select(row => new AssistantSession
        {
            Id = row["Id"].ToString()!,
            SessionDate = DateTime.Parse(row["SessionDate"].ToString()!),
            CreatedAtUtc = DateTime.Parse(row["CreatedAtUtc"].ToString()!),
            ArchivedAtUtc = row["ArchivedAtUtc"] == null ? null : DateTime.Parse(row["ArchivedAtUtc"].ToString()!)
        }).ToList();
    }

    public async Task ArchiveOldSessionsAsync(int olderThanDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        const string sql = """
            UPDATE AssistantSessions
            SET ArchivedAtUtc = @archivedAt
            WHERE CreatedAtUtc < @cutoff AND ArchivedAtUtc IS NULL
            """;

        var parameters = new Dictionary<string, object>
        {
            ["cutoff"] = cutoff.ToString("O"),
            ["archivedAt"] = DateTime.UtcNow.ToString("O")
        };

        await db.ExecuteNonQueryAsync(sql, parameters);
    }
}
