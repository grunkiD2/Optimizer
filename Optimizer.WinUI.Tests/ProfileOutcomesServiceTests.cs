using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Data;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ProfileOutcomesServiceTests : IAsyncLifetime
{
    private string _root = "";
    private string _dbPath = "";
    private DatabaseService _db = null!;

    public async Task InitializeAsync()
    {
        // Same DB-bootstrap as FancontrolTelemetryServiceTests / RetentionServiceTests:
        // a real DatabaseService over a temp file, schema applied via InitializeAsync().
        _root = Directory.CreateTempSubdirectory("po").FullName;
        _dbPath = Path.Combine(_root, "test.db");
        _db = new DatabaseService(_dbPath);
        await _db.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    // The production connection factory is DatabaseService.CreateConnection (closed connection,
    // opened by the service). Tests reuse the SAME factory so they exercise the real plumbing.
    private Func<SqliteConnection> Factory() => () => _db.CreateConnection();

    private async Task InsertTimelineAsync(string profile, string startTs, string? endTs, string? exe)
        => await _db.ExecuteNonQueryAsync(
            "INSERT INTO ProfileTimeline (ProfileName, StartTs, EndTs, Exe) VALUES (@p, @s, @e, @x)",
            new Dictionary<string, object>
            {
                ["p"] = profile,
                ["s"] = startTs,
                ["e"] = (object?)endTs ?? DBNull.Value,
                ["x"] = (object?)exe ?? DBNull.Value,
            });

    private async Task InsertTelemetryAsync(string ts, double coolant)
        => await _db.ExecuteNonQueryAsync(
            "INSERT INTO FancontrolTelemetry (Ts, Mode, Coolant) VALUES (@ts, 'TEST', @c)",
            new Dictionary<string, object> { ["ts"] = ts, ["c"] = coolant });

    [Fact]
    public async Task Rollup_windows_telemetry_by_profile_interval_and_computes_coolant_p95()
    {
        // Arrange: one timeline interval [t0, t1) for "AAA-HDR" + telemetry rows in the window.
        await InsertTimelineAsync("AAA-HDR", "2026-06-12T12:00:00Z", "2026-06-12T13:00:00Z", "destiny2.exe");
        for (int i = 0; i < 20; i++)
            await InsertTelemetryAsync(ts: $"2026-06-12T12:{i:00}:00Z", coolant: 30 + i); // 30..49

        var svc = new ProfileOutcomesService(Factory());
        var rec = await svc.RollupIntervalAsync("AAA-HDR", "2026-06-12T12:00:00Z", "2026-06-12T13:00:00Z");

        Assert.NotNull(rec);
        Assert.Equal(20, rec!.SampleCount);
        Assert.InRange(rec.CoolantP95!.Value, 47, 49); // p95 of 30..49 ~= 48-49
    }

    [Fact]
    public async Task LastVsPrevious_returns_two_most_recent_outcomes_for_delta()
    {
        var svc = new ProfileOutcomesService(Factory());
        await svc.SaveAsync(new OutcomeRecord("AAA-HDR", "2026-06-12T13:00:00Z", 60, 700, 43.2, 78));
        await svc.SaveAsync(new OutcomeRecord("AAA-HDR", "2026-06-13T13:00:00Z", 60, 700, 42.1, 85));

        var (latest, previous) = await svc.LastVsPreviousAsync("AAA-HDR");

        Assert.NotNull(latest); Assert.NotNull(previous);
        Assert.Equal(42.1, latest!.CoolantP95!.Value, 1);  // newest
        Assert.Equal(43.2, previous!.CoolantP95!.Value, 1); // previous
    }

    [Fact]
    public async Task Rollup_with_too_few_samples_yields_null_p95_not_a_crash()
    {
        await InsertTimelineAsync("Night", "2026-06-12T02:00:00Z", "2026-06-12T02:01:00Z", null);
        var svc = new ProfileOutcomesService(Factory());
        var rec = await svc.RollupIntervalAsync("Night", "2026-06-12T02:00:00Z", "2026-06-12T02:01:00Z");
        Assert.NotNull(rec);
        Assert.Equal(0, rec!.SampleCount);
        Assert.Null(rec.CoolantP95);
    }

    // --- ProfileTransitionWatcher: restart-rehydrate (no interval split) ---

    private static Mock<IFancontrolStatusService> StatusReturning(string? lastProfile, string? fgExe = null)
    {
        var mock = new Mock<IFancontrolStatusService>();
        mock.Setup(s => s.GetStatus()).Returns(() => new FancontrolStatus
        {
            Profiles = new FancontrolProfileStatus { LastAppliedProfile = lastProfile, ForegroundExe = fgExe },
        });
        return mock;
    }

    private async Task<int> CountIntervalsAsync(string profile)
        => Convert.ToInt32(await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM ProfileTimeline WHERE ProfileName = @p",
            new Dictionary<string, object> { ["p"] = profile }));

    private async Task<int> CountOpenIntervalsAsync(string profile)
        => Convert.ToInt32(await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM ProfileTimeline WHERE ProfileName = @p AND EndTs IS NULL",
            new Dictionary<string, object> { ["p"] = profile }));

    [Fact]
    public async Task Watcher_rehydrates_lastSeen_on_first_tick_and_does_not_split_a_still_active_interval()
    {
        // Arrange: a profile genuinely still active across an app restart → one OPEN interval in the DB.
        await InsertTimelineAsync("AAA-HDR", "2026-06-12T12:00:00Z", null, "destiny2.exe");
        Assert.Equal(1, await CountIntervalsAsync("AAA-HDR"));
        Assert.Equal(1, await CountOpenIntervalsAsync("AAA-HDR"));

        // A fresh singleton (in-memory _lastSeen = null) whose status reports the SAME profile.
        var clock = 0;
        Func<string> nowIso = () => $"2026-06-12T13:{clock++:00}:00Z";
        var status = StatusReturning("AAA-HDR", "destiny2.exe");
        var watcher = new ProfileTransitionWatcher(status.Object, Factory(), nowIso);

        // Act: first tick after "restart".
        await watcher.TickAsync();

        // Assert: rehydrate ADOPTED the open interval — no close, no new row, still exactly one open AAA-HDR.
        Assert.Equal(1, await CountIntervalsAsync("AAA-HDR"));
        Assert.Equal(1, await CountOpenIntervalsAsync("AAA-HDR"));

        // Now a GENUINE transition: status flips to a different profile.
        status.Setup(s => s.GetStatus()).Returns(() => new FancontrolStatus
        {
            Profiles = new FancontrolProfileStatus { LastAppliedProfile = "Desktop", ForegroundExe = "explorer.exe" },
        });
        await watcher.TickAsync();

        // Assert: AAA-HDR is now CLOSED (no open row), and a NEW open Desktop interval exists.
        Assert.Equal(1, await CountIntervalsAsync("AAA-HDR"));         // unchanged count
        Assert.Equal(0, await CountOpenIntervalsAsync("AAA-HDR"));     // closed
        Assert.Equal(1, await CountOpenIntervalsAsync("Desktop"));     // single new transition
    }
}
