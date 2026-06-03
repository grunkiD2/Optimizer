using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>Aggregates assistant action outcomes into per-tool, per-context metrics.</summary>
public class ActionAnalyticsService(DatabaseService db) : IActionAnalyticsService
{
    public async Task<List<ToolContextMetrics>> GetToolMetricsAsync(string? context = null)
    {
        var whereClause = "";
        var parameters = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(context))
        {
            whereClause = "WHERE DetectedContext = @context";
            parameters["context"] = context;
        }

        var sql = $"""
            SELECT
                ToolId,
                COALESCE(DetectedContext, 'Unknown') AS Context,
                COUNT(*) AS TotalInvocations,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessfulInvocations,
                AVG(ExecutionTimeMs) AS AverageDurationMs,
                MAX(ExecutedAtUtc) AS LastInvokedUtc
            FROM AssistantActions
            {whereClause}
            GROUP BY ToolId, COALESCE(DetectedContext, 'Unknown')
            ORDER BY TotalInvocations DESC
            """;

        return await QueryMetricsAsync(sql, parameters);
    }

    public async Task<List<ToolContextMetrics>> GetTopToolsAsync(int count = 10)
    {
        const string sql = """
            SELECT
                ToolId,
                COALESCE(DetectedContext, 'Unknown') AS Context,
                COUNT(*) AS TotalInvocations,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessfulInvocations,
                AVG(ExecutionTimeMs) AS AverageDurationMs,
                MAX(ExecutedAtUtc) AS LastInvokedUtc
            FROM AssistantActions
            GROUP BY ToolId, COALESCE(DetectedContext, 'Unknown')
            ORDER BY TotalInvocations DESC
            LIMIT @count
            """;

        var parameters = new Dictionary<string, object> { ["count"] = count };
        return await QueryMetricsAsync(sql, parameters);
    }

    public async Task<List<ToolContextMetrics>> GetMostReliableToolsAsync(string context, int count = 5)
    {
        const string sql = """
            SELECT
                ToolId,
                COALESCE(DetectedContext, 'Unknown') AS Context,
                COUNT(*) AS TotalInvocations,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessfulInvocations,
                AVG(ExecutionTimeMs) AS AverageDurationMs,
                MAX(ExecutedAtUtc) AS LastInvokedUtc
            FROM AssistantActions
            WHERE DetectedContext = @context
            GROUP BY ToolId
            HAVING COUNT(*) >= 3
            ORDER BY (CAST(SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS REAL) / COUNT(*)) DESC,
                     TotalInvocations DESC
            LIMIT @count
            """;

        var parameters = new Dictionary<string, object>
        {
            ["context"] = context,
            ["count"] = count
        };
        return await QueryMetricsAsync(sql, parameters);
    }

    public async Task<List<ToolContextMetrics>> GetProblematicToolsAsync(int count = 5)
    {
        const string sql = """
            SELECT
                ToolId,
                COALESCE(DetectedContext, 'Unknown') AS Context,
                COUNT(*) AS TotalInvocations,
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS SuccessfulInvocations,
                AVG(ExecutionTimeMs) AS AverageDurationMs,
                MAX(ExecutedAtUtc) AS LastInvokedUtc
            FROM AssistantActions
            GROUP BY ToolId, COALESCE(DetectedContext, 'Unknown')
            HAVING COUNT(*) >= 2
               AND (CAST(SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS REAL) / COUNT(*)) < 0.8
            ORDER BY (CAST(SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END) AS REAL) / COUNT(*)) ASC
            LIMIT @count
            """;

        var parameters = new Dictionary<string, object> { ["count"] = count };
        return await QueryMetricsAsync(sql, parameters);
    }

    public async Task RecalculateMetricsAsync()
    {
        // Rebuild the ToolContextMetrics summary table from the raw action log.
        // This is the materialized rollup used for fast reads elsewhere.
        await db.ExecuteNonQueryAsync("DELETE FROM ToolContextMetrics");

        const string sql = """
            INSERT INTO ToolContextMetrics
                (ToolId, Context, TotalInvocations, SuccessfulInvocations, AverageDurationMs, LastInvokedUtc, UpdatedAt)
            SELECT
                ToolId,
                COALESCE(DetectedContext, 'Unknown'),
                COUNT(*),
                SUM(CASE WHEN Success = 1 THEN 1 ELSE 0 END),
                AVG(ExecutionTimeMs),
                MAX(ExecutedAtUtc),
                @updatedAt
            FROM AssistantActions
            GROUP BY ToolId, COALESCE(DetectedContext, 'Unknown')
            """;

        var parameters = new Dictionary<string, object>
        {
            ["updatedAt"] = DateTime.UtcNow.ToString("O")
        };

        await db.ExecuteNonQueryAsync(sql, parameters);
        EngineLog.Write("Recalculated tool/context metrics rollup");
    }

    private async Task<List<ToolContextMetrics>> QueryMetricsAsync(string sql, Dictionary<string, object> parameters)
    {
        var rows = await db.ExecuteQueryAsync(sql, parameters);
        return rows.Select(row => new ToolContextMetrics
        {
            ToolId = row["ToolId"].ToString()!,
            Context = row["Context"]?.ToString() ?? "Unknown",
            TotalInvocations = Convert.ToInt32(row["TotalInvocations"]),
            SuccessfulInvocations = Convert.ToInt32(row["SuccessfulInvocations"]),
            AverageDurationMs = row["AverageDurationMs"] == null ? 0 : Convert.ToDouble(row["AverageDurationMs"]),
            LastInvokedUtc = row["LastInvokedUtc"] == null ? null : DateTime.Parse(row["LastInvokedUtc"].ToString()!)
        }).ToList();
    }
}
