using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Mines <c>ProfileApplications</c> for recurring time-of-day patterns and proposes
/// TimeRange automation rules. A suggestion is raised when the same profile is applied
/// within a similar 2-hour window on at least <see cref="MinOccurrences"/> distinct days.
/// </summary>
public class RuleSuggestionService(DatabaseService db, IProfileAutomationService automation) : IRuleSuggestionService
{
    private const int MinOccurrences = 4;

    public async Task GenerateSuggestionsAsync(int lookbackDays = 30)
    {
        var cutoff = DateTime.UtcNow.AddDays(-lookbackDays);

        const string sql = """
            SELECT ProfileId, Context, AppliedAtUtc
            FROM ProfileApplications
            WHERE AppliedAtUtc >= @cutoff
            ORDER BY AppliedAtUtc ASC
            """;

        var rows = await db.ExecuteQueryAsync(sql,
            new Dictionary<string, object> { ["cutoff"] = cutoff.ToString("O") });

        // Bucket applications by (profile, local 2-hour slot), counting distinct days.
        var buckets = new Dictionary<(string profile, int slot), HashSet<DateOnly>>();

        foreach (var row in rows)
        {
            var profileId = row.GetString("ProfileId");
            var appliedAt = row.GetDateTime("AppliedAtUtc").ToLocalTime();
            var slot = appliedAt.Hour / 2; // 0..11 (two-hour slots)
            var day = DateOnly.FromDateTime(appliedAt);

            var key = (profileId, slot);
            if (!buckets.TryGetValue(key, out var days))
                buckets[key] = days = new HashSet<DateOnly>();
            days.Add(day);
        }

        // Existing pending/rejected suggestions we shouldn't duplicate.
        var existing = await GetExistingSignaturesAsync();

        foreach (var ((profileId, slot), days) in buckets)
        {
            if (days.Count < MinOccurrences) continue;

            var startHour = slot * 2;
            var endHour = Math.Min(24, startHour + 2);
            var triggerValue = $"{startHour:00}:00-{endHour % 24:00}:00";
            var signature = $"{profileId}|TimeRange|{triggerValue}";
            if (existing.Contains(signature)) continue;

            // Confidence scales with how consistently the pattern recurs.
            var confidence = Math.Min(1.0, days.Count / (double)Math.Max(MinOccurrences, lookbackDays / 4));

            var suggestion = new SuggestedAutomationRule
            {
                ProfileId = profileId,
                ProfileName = profileId,
                TriggerType = "TimeRange",
                TriggerValue = triggerValue,
                ConfidenceScore = confidence,
                ReasoningText = $"Applied on {days.Count} days around {startHour:00}:00–{endHour % 24:00}:00."
            };

            await InsertSuggestionAsync(suggestion);
        }

        EngineLog.Write("Rule suggestion pass complete");
    }

    public async Task<List<SuggestedAutomationRule>> GetPendingSuggestionsAsync()
    {
        const string sql = """
            SELECT Id, ProfileId, ProfileName, TriggerType, TriggerValue,
                   ConfidenceScore, ReasoningText, CreatedAtUtc
            FROM SuggestedRules
            WHERE Status = 'Pending'
            ORDER BY ConfidenceScore DESC, CreatedAtUtc DESC
            """;

        var rows = await db.ExecuteQueryAsync(sql);
        return rows.Select(row => new SuggestedAutomationRule
        {
            Id = row.GetString("Id"),
            ProfileId = row.GetString("ProfileId"),
            ProfileName = row.GetString("ProfileName"),
            TriggerType = row.GetString("TriggerType"),
            TriggerValue = row.GetString("TriggerValue"),
            ConfidenceScore = row.GetDouble("ConfidenceScore"),
            ReasoningText = row.GetString("ReasoningText"),
            CreatedAtUtc = row.GetDateTime("CreatedAtUtc")
        }).ToList();
    }

    public async Task AcceptSuggestionAsync(string suggestionId)
    {
        // Audit C13: accepting a suggestion used to ONLY flip a DB status that nothing read —
        // no automation rule was ever created, so "Accepted" was a lie. Now we materialise a
        // real ProfileRule via the automation service, then mark it accepted.
        var rows = await db.ExecuteQueryAsync(
            "SELECT ProfileId, ProfileName, TriggerType, TriggerValue FROM SuggestedRules WHERE Id = @id",
            new Dictionary<string, object> { ["id"] = suggestionId });
        var row = rows.FirstOrDefault();
        if (row != null)
        {
            var profileId = row.GetString("ProfileId");
            var profileName = row.GetString("ProfileName");
            var triggerType = row.GetString("TriggerType");
            var triggerValue = row.GetString("TriggerValue");

            var rule = new ProfileRule
            {
                Name = $"{profileName} ({triggerValue})",
                ProfileId = profileId,
                ProfileName = profileName,
                IsEnabled = true,
            };
            if (string.Equals(triggerType, "TimeRange", StringComparison.OrdinalIgnoreCase)
                && TryParseTimeRange(triggerValue, out var start, out var end))
            {
                rule.Trigger = RuleTrigger.TimeRange;
                rule.StartTime = start;
                rule.EndTime = end;
            }
            else
            {
                rule.Trigger = RuleTrigger.ProcessRunning;
                rule.ProcessName = triggerValue;
            }
            await automation.AddRuleAsync(rule);
            EngineLog.Write($"Rule suggestion accepted → created rule '{rule.Name}'");
        }

        await SetStatusAsync(suggestionId, "Accepted");
    }

    /// <summary>Parses the "HH:00-HH:00" trigger value the generator emits.</summary>
    private static bool TryParseTimeRange(string value, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        var parts = (value ?? "").Split('-', 2);
        return parts.Length == 2
            && TimeSpan.TryParse(parts[0].Trim(), out start)
            && TimeSpan.TryParse(parts[1].Trim(), out end);
    }

    public Task RejectSuggestionAsync(string suggestionId) => SetStatusAsync(suggestionId, "Rejected");

    private async Task SetStatusAsync(string suggestionId, string status)
    {
        await db.ExecuteNonQueryAsync(
            "UPDATE SuggestedRules SET Status = @status, UpdatedAt = @now WHERE Id = @id",
            new Dictionary<string, object>
            {
                ["status"] = status,
                ["now"] = DateTime.UtcNow.ToString("O"),
                ["id"] = suggestionId
            });
    }

    private async Task<HashSet<string>> GetExistingSignaturesAsync()
    {
        // Any non-pending (accepted/rejected) or already-pending suggestion counts as "seen".
        const string sql = "SELECT ProfileId, TriggerType, TriggerValue FROM SuggestedRules";
        var rows = await db.ExecuteQueryAsync(sql);
        return rows
            .Select(r => $"{r["ProfileId"]}|{r["TriggerType"]}|{r["TriggerValue"]}")
            .ToHashSet();
    }

    private async Task InsertSuggestionAsync(SuggestedAutomationRule s)
    {
        const string sql = """
            INSERT INTO SuggestedRules
                (Id, ProfileId, ProfileName, TriggerType, TriggerValue,
                 ConfidenceScore, ReasoningText, Status, CreatedAtUtc, UpdatedAt)
            VALUES
                (@id, @profileId, @profileName, @triggerType, @triggerValue,
                 @confidence, @reasoning, 'Pending', @createdAt, @createdAt)
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["id"] = s.Id,
            ["profileId"] = s.ProfileId,
            ["profileName"] = s.ProfileName,
            ["triggerType"] = s.TriggerType,
            ["triggerValue"] = s.TriggerValue,
            ["confidence"] = s.ConfidenceScore,
            ["reasoning"] = s.ReasoningText,
            ["createdAt"] = s.CreatedAtUtc.ToString("O")
        });
    }
}
