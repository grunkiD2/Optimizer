using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>SQLite-backed anomaly detector with per-metric learned baselines and suppression.</summary>
public class AnomalyDetector(DatabaseService db) : IAnomalyDetector
{
    private const int MinSamples = 20;          // need enough history before flagging
    private const double SigmaThreshold = 2.0;  // flag beyond 2σ
    private const int SuppressAfterDismissals = 3;

    public async Task RecordSampleAsync(string context, string metric, double value)
    {
        var acc = await LoadBaselineAsync(context, metric);
        var updated = acc.Add(value);

        const string sql = """
            INSERT INTO MetricBaselines (Context, Metric, SampleCount, Mean, M2, UpdatedAt)
            VALUES (@context, @metric, @count, @mean, @m2, @now)
            ON CONFLICT(Context, Metric) DO UPDATE SET
                SampleCount = @count, Mean = @mean, M2 = @m2, UpdatedAt = @now
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["metric"] = metric,
            ["count"] = updated.Count,
            ["mean"] = updated.Mean,
            ["m2"] = updated.M2,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
    }

    public async Task<List<AnomalyResult>> EvaluateAsync(string context, IReadOnlyDictionary<string, double> readings)
    {
        var results = new List<AnomalyResult>();

        foreach (var (metric, value) in readings)
        {
            if (await IsSuppressedAsync(context, metric)) continue;

            var acc = await LoadBaselineAsync(context, metric);
            if (acc.Count < MinSamples) continue;

            var sigma = acc.SigmaOf(value);
            if (sigma >= SigmaThreshold)
            {
                var anomaly = new AnomalyResult
                {
                    Context = context,
                    Metric = metric,
                    Value = value,
                    Expected = acc.Mean,
                    Sigma = sigma
                };
                results.Add(anomaly);
                await PersistAlertAsync(anomaly);
            }
        }

        return results;
    }

    public async Task DismissAsync(string context, string metric)
    {
        const string sql = """
            INSERT INTO AnomalySuppressions (Context, Metric, DismissCount, UpdatedAt)
            VALUES (@context, @metric, 1, @now)
            ON CONFLICT(Context, Metric) DO UPDATE SET
                DismissCount = DismissCount + 1, UpdatedAt = @now
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["metric"] = metric,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
    }

    private async Task<WelfordAccumulator> LoadBaselineAsync(string context, string metric)
    {
        const string sql = """
            SELECT SampleCount, Mean, M2 FROM MetricBaselines
            WHERE Context = @context AND Metric = @metric
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["metric"] = metric
        });

        if (rows.Count == 0) return new WelfordAccumulator(0, 0, 0);
        var r = rows[0];
        return new WelfordAccumulator(
            Convert.ToInt64(r["SampleCount"]),
            Convert.ToDouble(r["Mean"]),
            Convert.ToDouble(r["M2"]));
    }

    private async Task<bool> IsSuppressedAsync(string context, string metric)
    {
        const string sql = """
            SELECT DismissCount FROM AnomalySuppressions
            WHERE Context = @context AND Metric = @metric
            """;
        var count = await db.ExecuteScalarAsync<long>(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["metric"] = metric
        });
        return count >= SuppressAfterDismissals;
    }

    private async Task PersistAlertAsync(AnomalyResult a)
    {
        const string sql = """
            INSERT INTO AnomalyAlerts (Context, Metric, Value, Expected, Sigma, CreatedAtUtc)
            VALUES (@context, @metric, @value, @expected, @sigma, @now)
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = a.Context,
            ["metric"] = a.Metric,
            ["value"] = a.Value,
            ["expected"] = a.Expected,
            ["sigma"] = a.Sigma,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
    }
}
