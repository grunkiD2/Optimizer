using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Logs assistant tool invocations to SQLite for analytics and learning.</summary>
public class AssistantActionLogger(DatabaseService db) : IAssistantActionLogger
{
    public async Task LogActionAsync(
        string toolId,
        string? arguments,
        bool success,
        string? errorMessage = null,
        int executionTimeMs = 0,
        string? detectedContext = null)
    {
        const string sql = """
            INSERT INTO AssistantActions
            (ToolId, Arguments, Success, ErrorMessage, ExecutedAtUtc, ExecutionTimeMs, DetectedContext)
            VALUES (@toolId, @arguments, @success, @errorMessage, @executedAt, @duration, @context)
            """;

        var parameters = new Dictionary<string, object>
        {
            ["toolId"] = toolId,
            ["arguments"] = arguments ?? "",
            ["success"] = success ? 1 : 0,
            ["errorMessage"] = errorMessage ?? "",
            ["executedAt"] = DateTime.UtcNow.ToString("O"),
            ["duration"] = executionTimeMs,
            ["context"] = detectedContext ?? "Unknown"
        };

        await db.ExecuteNonQueryAsync(sql, parameters);
        EngineLog.Write($"Logged action: {toolId} (success={success}, context={detectedContext})");
    }

    public async Task<ToolActionMetrics?> GetMetricsAsync(string toolId, string? context = null)
    {
        var whereClause = "WHERE ToolId = @toolId";
        var parameters = new Dictionary<string, object> { ["toolId"] = toolId };

        if (!string.IsNullOrEmpty(context))
        {
            whereClause += " AND DetectedContext = @context";
            parameters["context"] = context;
        }

        var sql = $"""
            SELECT
                ToolId,
                DetectedContext as Context,
                COUNT(*) as TotalInvocations,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) as SuccessfulInvocations,
                AVG(ExecutionTimeMs) as AverageDurationMs,
                MAX(ExecutedAtUtc) as LastInvokedUtc
            FROM AssistantActions
            {whereClause}
            GROUP BY ToolId, DetectedContext
            """;

        var results = await db.ExecuteQueryAsync(sql, parameters);
        if (results.Count == 0) return null;

        var row = results[0];
        return new ToolActionMetrics
        {
            ToolId = toolId,
            Context = context,
            TotalInvocations = row.GetInt("TotalInvocations"),
            SuccessfulInvocations = row.GetInt("SuccessfulInvocations"),
            AverageDurationMs = row.GetDouble("AverageDurationMs"),
            LastInvokedUtc = row.GetDateTimeOrNull("LastInvokedUtc")
        };
    }

    public async Task<List<AssistantActionLog>> GetRecentActionsAsync(int dayCount = 30)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-dayCount);
        const string sql = """
            SELECT
                Id,
                ToolId,
                Arguments,
                Success,
                ErrorMessage,
                ExecutedAtUtc,
                ExecutionTimeMs,
                DetectedContext
            FROM AssistantActions
            WHERE ExecutedAtUtc >= @cutoffDate
            ORDER BY ExecutedAtUtc DESC
            LIMIT 1000
            """;

        var parameters = new Dictionary<string, object>
        {
            ["cutoffDate"] = cutoffDate.ToString("O")
        };

        var results = await db.ExecuteQueryAsync(sql, parameters);
        return results.Select(row => new AssistantActionLog
        {
            Id = row.GetInt("Id"),
            ToolId = row.GetString("ToolId"),
            Arguments = row.GetStringOrNull("Arguments"),
            Success = row.GetBool("Success"),
            ErrorMessage = row.GetStringOrNull("ErrorMessage"),
            ExecutedAtUtc = row.GetDateTime("ExecutedAtUtc"),
            ExecutionTimeMs = row.GetInt("ExecutionTimeMs"),
            DetectedContext = row.GetStringOrNull("DetectedContext")
        }).ToList();
    }
}
