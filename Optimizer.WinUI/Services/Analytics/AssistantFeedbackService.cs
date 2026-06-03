using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>Persists user feedback on assistant actions to SQLite.</summary>
public class AssistantFeedbackService(DatabaseService db) : IAssistantFeedbackService
{
    public async Task RecordFeedbackAsync(string? sessionId, string toolId, FeedbackVerdict verdict, string? comment = null)
    {
        const string sql = """
            INSERT INTO AssistantFeedback (SessionId, ToolId, UserFeedback, Comment, CreatedAtUtc)
            VALUES (@sessionId, @toolId, @verdict, @comment, @createdAt)
            """;

        var parameters = new Dictionary<string, object>
        {
            ["sessionId"] = sessionId ?? "",
            ["toolId"] = toolId,
            ["verdict"] = verdict.ToString(),
            ["comment"] = comment ?? "",
            ["createdAt"] = DateTime.UtcNow.ToString("O")
        };

        await db.ExecuteNonQueryAsync(sql, parameters);
        EngineLog.Write($"Recorded feedback: {toolId} = {verdict}");
    }

    public async Task<int> GetNetScoreAsync(string toolId)
    {
        const string sql = """
            SELECT
                SUM(CASE WHEN UserFeedback = 'Liked' THEN 1 ELSE 0 END) -
                SUM(CASE WHEN UserFeedback = 'Disliked' THEN 1 ELSE 0 END) AS NetScore
            FROM AssistantFeedback
            WHERE ToolId = @toolId
            """;

        var parameters = new Dictionary<string, object> { ["toolId"] = toolId };
        var score = await db.ExecuteScalarAsync<long>(sql, parameters);
        return (int)score;
    }

    public async Task<List<AssistantFeedbackEntry>> GetRecentFeedbackAsync(int count = 50)
    {
        const string sql = """
            SELECT Id, SessionId, ToolId, UserFeedback, Comment, CreatedAtUtc
            FROM AssistantFeedback
            ORDER BY CreatedAtUtc DESC
            LIMIT @count
            """;

        var parameters = new Dictionary<string, object> { ["count"] = count };
        var rows = await db.ExecuteQueryAsync(sql, parameters);

        return rows.Select(row => new AssistantFeedbackEntry
        {
            Id = Convert.ToInt32(row["Id"]),
            SessionId = string.IsNullOrEmpty(row["SessionId"]?.ToString()) ? null : row["SessionId"].ToString(),
            ToolId = row["ToolId"].ToString()!,
            Verdict = Enum.Parse<FeedbackVerdict>(row["UserFeedback"].ToString()!),
            Comment = string.IsNullOrEmpty(row["Comment"]?.ToString()) ? null : row["Comment"].ToString(),
            CreatedAtUtc = DateTime.Parse(row["CreatedAtUtc"].ToString()!)
        }).ToList();
    }
}
