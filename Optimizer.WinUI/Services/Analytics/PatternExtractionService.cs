using System.Text.Json;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Mines the assistant action log for recurring n-gram tool sequences that succeed
/// within a context, and persists them as <see cref="LearnedPattern"/>s.
/// </summary>
public class PatternExtractionService(
    DatabaseService db,
    IAssistantActionLogger actionLogger) : IPatternExtractionService
{
    // Sequences of 2..MaxSequenceLength consecutive actions are considered.
    private const int MaxSequenceLength = 3;
    private const int MinObservations = 2;

    public async Task ExtractPatternsAsync(int lookbackDays = 30)
    {
        var actions = await actionLogger.GetRecentActionsAsync(lookbackDays);
        if (actions.Count < 2) return;

        // Order chronologically (GetRecentActionsAsync returns newest-first).
        var ordered = actions.OrderBy(a => a.ExecutedAtUtc).ToList();

        // Build candidate sequences grouped by context. We slide a window over the
        // timeline; a "session boundary" is a >10 minute gap between actions.
        var candidates = new Dictionary<string, PatternAccumulator>();

        for (int i = 0; i < ordered.Count; i++)
        {
            for (int len = 2; len <= MaxSequenceLength && i + len <= ordered.Count; len++)
            {
                var window = ordered.GetRange(i, len);

                // Reject windows that span a session gap or mix contexts.
                if (!IsContiguousSession(window)) continue;
                var context = window[0].DetectedContext ?? "Unknown";
                if (window.Any(w => (w.DetectedContext ?? "Unknown") != context)) continue;

                var sequence = window.Select(w => w.ToolId).ToList();
                var key = context + "|" + string.Join(">", sequence);

                if (!candidates.TryGetValue(key, out var acc))
                {
                    acc = new PatternAccumulator
                    {
                        Context = context,
                        Sequence = sequence,
                        FirstObservedUtc = window[0].ExecutedAtUtc
                    };
                    candidates[key] = acc;
                }

                acc.ObservedCount++;
                if (window.All(w => w.Success)) acc.SuccessfulCount++;
                if (window[0].ExecutedAtUtc < acc.FirstObservedUtc)
                    acc.FirstObservedUtc = window[0].ExecutedAtUtc;
            }
        }

        // Keep only sequences seen at least MinObservations times.
        var kept = candidates.Values
            .Where(c => c.ObservedCount >= MinObservations)
            .ToList();

        // Rebuild the learned-pattern table atomically: a crash mid-rebuild must not leave a
        // half-populated (or empty) table behind.
        await db.RunInTransactionAsync(async batch =>
        {
            await batch.ExecuteNonQueryAsync("DELETE FROM LearnedPatterns");
            foreach (var c in kept)
            {
                await batch.ExecuteNonQueryAsync(UpsertPatternSql, new Dictionary<string, object>
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["description"] = BuildDescription(c.Context, c.Sequence),
                    ["sequence"] = JsonSerializer.Serialize(c.Sequence),
                    ["context"] = c.Context,
                    ["observed"] = c.ObservedCount,
                    ["successful"] = c.SuccessfulCount,
                    ["firstObserved"] = c.FirstObservedUtc.ToString("O"),
                    ["updatedAt"] = DateTime.UtcNow.ToString("O")
                });
            }
        });

        EngineLog.Write($"Extracted {kept.Count} learned pattern(s) from {actions.Count} actions");
    }

    public async Task<List<LearnedPattern>> GetPatternsAsync(string? context = null, int count = 10)
    {
        var whereClause = "";
        var parameters = new Dictionary<string, object> { ["count"] = count };

        if (!string.IsNullOrEmpty(context))
        {
            whereClause = "WHERE ApplicableContext = @context";
            parameters["context"] = context;
        }

        var sql = $"""
            SELECT Id, Description, ActionSequence, ApplicableContext,
                   ObservedCount, SuccessfulCount, FirstObservedUtc
            FROM LearnedPatterns
            {whereClause}
            ORDER BY (CAST(SuccessfulCount AS REAL) / NULLIF(ObservedCount, 0)) DESC,
                     ObservedCount DESC
            LIMIT @count
            """;

        var rows = await db.ExecuteQueryAsync(sql, parameters);
        return rows.Select(MapPattern).ToList();
    }

    public async Task<LearnedPattern?> GetBestPatternAsync(string context)
    {
        var patterns = await GetPatternsAsync(context, 1);
        var best = patterns.FirstOrDefault();
        // Only surface a pattern if it clears a basic confidence bar.
        return best is { Confidence: >= 0.6 } ? best : null;
    }

    private const string UpsertPatternSql = """
        INSERT INTO LearnedPatterns
            (Id, Description, ActionSequence, ApplicableContext,
             ObservedCount, SuccessfulCount, FirstObservedUtc, UpdatedAt)
        VALUES
            (@id, @description, @sequence, @context,
             @observed, @successful, @firstObserved, @updatedAt)
        """;

    private static LearnedPattern MapPattern(DbRow row)
    {
        var sequenceJson = row.GetStringOrNull("ActionSequence") ?? "[]";
        var sequence = JsonSerializer.Deserialize<List<string>>(sequenceJson) ?? new();

        return new LearnedPattern
        {
            Id = row.GetString("Id"),
            Description = row.GetString("Description"),
            ActionSequence = sequence,
            ApplicableContext = row.GetStringOrNull("ApplicableContext"),
            ObservedCount = row.GetInt("ObservedCount"),
            SuccessfulCount = row.GetInt("SuccessfulCount"),
            FirstObservedUtc = row.GetDateTime("FirstObservedUtc")
        };
    }

    private static bool IsContiguousSession(List<AssistantActionLog> window)
    {
        for (int i = 1; i < window.Count; i++)
        {
            var gap = window[i].ExecutedAtUtc - window[i - 1].ExecutedAtUtc;
            if (gap > TimeSpan.FromMinutes(10)) return false;
        }
        return true;
    }

    private static string BuildDescription(string context, List<string> sequence)
        => $"In {context}: {string.Join(" → ", sequence)}";

    private sealed class PatternAccumulator
    {
        public string Context { get; set; } = "";
        public List<string> Sequence { get; set; } = new();
        public int ObservedCount { get; set; }
        public int SuccessfulCount { get; set; }
        public DateTime FirstObservedUtc { get; set; }
    }
}
