using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Optimizer.WinUI.Services.Assistant;
using Optimizer.WinUI.Services.Data;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>Shared temp-file SQLite database for learning-engine integration tests.</summary>
public abstract class DbTestBase : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"optimizer-test-{Guid.NewGuid():N}.db");
    protected DatabaseService Db = null!;

    public async Task InitializeAsync()
    {
        Db = new DatabaseService(_dbPath);
        await Db.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>Insert a ProfileApplications row with a controlled applied-time.</summary>
    protected Task InsertApplicationAsync(string profileId, string context, DateTime appliedAtUtc) =>
        Db.ExecuteNonQueryAsync(
            "INSERT INTO ProfileApplications (ProfileId, Context, AppliedAtUtc, Resolved) VALUES (@p,@c,@t,0)",
            new Dictionary<string, object> { ["p"] = profileId, ["c"] = context, ["t"] = appliedAtUtc.ToString("O") });
}

public class ActionAnalyticsServiceTests : DbTestBase
{
    [Fact]
    public async Task GetToolMetrics_computes_success_rate_per_context()
    {
        var logger = new AssistantActionLogger(Db);
        await logger.LogActionAsync("apply_profile", "", true, detectedContext: "Gaming");
        await logger.LogActionAsync("apply_profile", "", true, detectedContext: "Gaming");
        await logger.LogActionAsync("apply_profile", "", true, detectedContext: "Gaming");
        await logger.LogActionAsync("apply_profile", "", false, detectedContext: "Gaming");

        var metrics = await new ActionAnalyticsService(Db).GetToolMetricsAsync("Gaming");

        var m = Assert.Single(metrics);
        Assert.Equal("apply_profile", m.ToolId);
        Assert.Equal(4, m.TotalInvocations);
        Assert.Equal(3, m.SuccessfulInvocations);
        Assert.Equal(0.75, m.SuccessRate, 3);
    }

    [Fact]
    public async Task MostReliable_requires_at_least_three_invocations()
    {
        var logger = new AssistantActionLogger(Db);
        await logger.LogActionAsync("rare", "", true, detectedContext: "Work"); // 1 → excluded
        for (var i = 0; i < 3; i++) await logger.LogActionAsync("common", "", true, detectedContext: "Work");

        var reliable = await new ActionAnalyticsService(Db).GetMostReliableToolsAsync("Work");

        Assert.Equal("common", Assert.Single(reliable).ToolId);
    }

    [Fact]
    public async Task Recalculate_rebuilds_the_rollup_table()
    {
        var logger = new AssistantActionLogger(Db);
        await logger.LogActionAsync("x", "", true, detectedContext: "Plex");
        var analytics = new ActionAnalyticsService(Db);

        await analytics.RecalculateMetricsAsync();

        var rows = await Db.ExecuteQueryAsync("SELECT ToolId, TotalInvocations FROM ToolContextMetrics");
        var r = Assert.Single(rows);
        Assert.Equal("x", r.GetString("ToolId"));
        Assert.Equal(1, r.GetInt("TotalInvocations"));
    }
}

public class ProfileContextServiceTests : DbTestBase
{
    [Fact]
    public async Task Application_that_is_kept_counts_as_a_success()
    {
        await InsertApplicationAsync("gaming", "Gaming", DateTime.UtcNow.AddHours(-1));
        var svc = new ProfileContextService(Db);

        await svc.ResolvePendingAsync(TimeSpan.FromMinutes(30));

        var assoc = await svc.GetAssociationAsync("gaming", "Gaming");
        Assert.NotNull(assoc);
        Assert.Equal(1, assoc!.SuccessCount);
    }

    [Fact]
    public async Task Application_superseded_within_the_window_is_not_a_success()
    {
        var t0 = DateTime.UtcNow.AddHours(-1);
        await InsertApplicationAsync("gaming", "Gaming", t0);
        await InsertApplicationAsync("work", "Work", t0.AddMinutes(10)); // supersedes gaming within 30-min window
        var svc = new ProfileContextService(Db);

        await svc.ResolvePendingAsync(TimeSpan.FromMinutes(30));

        var gaming = await svc.GetAssociationAsync("gaming", "Gaming");
        Assert.True(gaming is null || gaming.SuccessCount == 0);
    }

    [Fact]
    public async Task Best_profiles_ranked_by_success_rate()
    {
        // good: 1 apply, kept. meh: applied then immediately superseded.
        var t0 = DateTime.UtcNow.AddHours(-2);
        await InsertApplicationAsync("good", "Gaming", t0);
        await InsertApplicationAsync("meh", "Gaming", t0.AddMinutes(40));
        await InsertApplicationAsync("other", "Gaming", t0.AddMinutes(45)); // supersedes meh
        var svc = new ProfileContextService(Db);
        // also bump apply counts so rates are comparable
        await svc.RecordApplicationAsync("good", "Gaming");
        await svc.RecordApplicationAsync("meh", "Gaming");

        await svc.ResolvePendingAsync(TimeSpan.FromMinutes(30));

        var best = await svc.GetBestProfilesForContextAsync("Gaming");
        Assert.NotEmpty(best);
        Assert.Equal("good", best[0].ProfileId); // highest success rate first
    }
}

public class AnomalyDetectorIntegrationTests : DbTestBase
{
    private static Dictionary<string, double> Reading(double cpu) => new() { ["cpu"] = cpu };

    private static async Task SeedBaselineAsync(AnomalyDetector d)
    {
        for (var i = 0; i < 25; i++) await d.RecordSampleAsync("Work", "cpu", 50 + (i % 2 == 0 ? 1 : -1));
    }

    [Fact]
    public async Task Flags_a_reading_far_outside_the_learned_baseline()
    {
        var d = new AnomalyDetector(Db);
        await SeedBaselineAsync(d);

        Assert.Single(await d.EvaluateAsync("Work", Reading(100)));
        Assert.Empty(await d.EvaluateAsync("Work", Reading(50)));
    }

    [Fact]
    public async Task Does_not_flag_until_enough_samples_exist()
    {
        var d = new AnomalyDetector(Db);
        for (var i = 0; i < 5; i++) await d.RecordSampleAsync("Work", "cpu", 50);

        Assert.Empty(await d.EvaluateAsync("Work", Reading(100)));
    }

    [Fact]
    public async Task Suppresses_a_metric_after_three_dismissals()
    {
        var d = new AnomalyDetector(Db);
        await SeedBaselineAsync(d);
        await d.DismissAsync("Work", "cpu");
        await d.DismissAsync("Work", "cpu");
        await d.DismissAsync("Work", "cpu");

        Assert.Empty(await d.EvaluateAsync("Work", Reading(100)));
    }
}

public class RuleSuggestionServiceIntegrationTests : DbTestBase
{
    [Fact]
    public async Task Suggests_a_time_rule_for_a_recurring_nightly_apply()
    {
        for (var day = 1; day <= 4; day++)
        {
            var local = DateTime.Now.Date.AddDays(-day).AddHours(22).AddMinutes(15);
            await InsertApplicationAsync("gaming", "Gaming", local.ToUniversalTime());
        }
        var svc = new RuleSuggestionService(Db);

        await svc.GenerateSuggestionsAsync();

        var s = Assert.Single(await svc.GetPendingSuggestionsAsync());
        Assert.Equal("gaming", s.ProfileId);
        Assert.Equal("TimeRange", s.TriggerType);

        await svc.AcceptSuggestionAsync(s.Id);
        Assert.Empty(await svc.GetPendingSuggestionsAsync());
    }

    [Fact]
    public async Task Does_not_suggest_below_the_occurrence_threshold()
    {
        for (var day = 1; day <= 2; day++) // only 2 days < MinOccurrences (4)
        {
            var local = DateTime.Now.Date.AddDays(-day).AddHours(22);
            await InsertApplicationAsync("gaming", "Gaming", local.ToUniversalTime());
        }
        var svc = new RuleSuggestionService(Db);

        await svc.GenerateSuggestionsAsync();

        Assert.Empty(await svc.GetPendingSuggestionsAsync());
    }
}

public class PredictiveAlertServiceIntegrationTests : DbTestBase
{
    private sealed class FakeMaintenance : IPredictiveMaintenanceService
    {
        public List<DriveSpaceForecast> Drives { get; } = new();
        public List<DiskFailureForecast> Disks { get; } = new();
        public Task<IReadOnlyList<DriveSpaceForecast>> ForecastDriveSpaceAsync() => Task.FromResult<IReadOnlyList<DriveSpaceForecast>>(Drives);
        public Task<IReadOnlyList<DiskFailureForecast>> ForecastDiskHealthAsync() => Task.FromResult<IReadOnlyList<DiskFailureForecast>>(Disks);
    }

    [Fact]
    public async Task Raises_a_drive_alert_once_and_dedupes_repeats()
    {
        var maint = new FakeMaintenance();
        maint.Drives.Add(new DriveSpaceForecast("C", 10, 100, 90, DaysUntilFull: 5, GbPerDay: 2));
        var svc = new PredictiveAlertService(Db, maint);

        Assert.Single(await svc.EvaluateAsync());   // first time → alert
        Assert.Empty(await svc.EvaluateAsync());     // same condition → deduped

        var active = await svc.GetActiveAlertsAsync();
        await svc.AcknowledgeAsync(Assert.Single(active).Id);
        Assert.Empty(await svc.GetActiveAlertsAsync());
    }

    [Fact]
    public async Task Healthy_forecasts_raise_nothing()
    {
        var maint = new FakeMaintenance();
        maint.Drives.Add(new DriveSpaceForecast("C", 500, 1000, 50, DaysUntilFull: 9999, GbPerDay: 0.1));
        var svc = new PredictiveAlertService(Db, maint);

        Assert.Empty(await svc.EvaluateAsync());
    }
}

public class PatternExtractionServiceIntegrationTests : DbTestBase
{
    private sealed class FakeLogger : IAssistantActionLogger
    {
        public List<AssistantActionLog> Actions { get; } = new();
        public Task LogActionAsync(string toolId, string? arguments, bool success, string? errorMessage = null, int executionTimeMs = 0, string? detectedContext = null) => Task.CompletedTask;
        public Task<ToolActionMetrics?> GetMetricsAsync(string toolId, string? context = null) => Task.FromResult<ToolActionMetrics?>(null);
        public Task<List<AssistantActionLog>> GetRecentActionsAsync(int dayCount = 30) => Task.FromResult(Actions);
    }

    [Fact]
    public async Task Mines_a_repeated_contiguous_successful_sequence()
    {
        var logger = new FakeLogger();
        var t = DateTime.UtcNow.AddHours(-1);
        void Add(string tool, DateTime when) =>
            logger.Actions.Add(new AssistantActionLog { ToolId = tool, Success = true, DetectedContext = "Gaming", ExecutedAtUtc = when });

        Add("a", t); Add("b", t.AddSeconds(5));                       // occurrence 1
        Add("a", t.AddMinutes(30)); Add("b", t.AddMinutes(30).AddSeconds(5)); // occurrence 2 (separate session)

        var svc = new PatternExtractionService(Db, logger);
        await svc.ExtractPatternsAsync();

        var patterns = await svc.GetPatternsAsync("Gaming");
        Assert.Contains(patterns, p => string.Join(">", p.ActionSequence) == "a>b");
    }
}
