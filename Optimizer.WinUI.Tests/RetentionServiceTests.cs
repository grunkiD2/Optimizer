using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Data;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class RetentionServiceTests : IAsyncLifetime
{
    private string _root = "";
    private DatabaseService _db = null!;

    public async Task InitializeAsync()
    {
        _root = Directory.CreateTempSubdirectory("retention").FullName;
        _db = new DatabaseService(Path.Combine(_root, "test.db"));
        await _db.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string DaysAgoIso(int days) => DateTime.UtcNow.AddDays(-days).ToString("O");
    private static string DaysAgoLocal(int days) => DateTimeOffset.Now.AddDays(-days).ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz");

    private async Task<long> CountAsync(string table)
        => await _db.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM {table}");

    [Fact]
    public async Task Sweep_prunes_old_rows_and_keeps_recent_ones()
    {
        // FancontrolTelemetry (30 d, local-offset Ts strings like the brain writes them)
        foreach (var (days, mode) in new[] { (45, "old"), (2, "new") })
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO FancontrolTelemetry (Ts, Mode) VALUES (@ts, @mode)",
                new Dictionary<string, object> { ["ts"] = DaysAgoLocal(days), ["mode"] = mode });

        // PowerSnapshots (14 d)
        foreach (var days in new[] { 20, 1 })
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO PowerSnapshots (Ts, Context, ProcessName, AvgPowerW, CpuShare, WindowSec) VALUES (@ts, 'Unknown', 'x', 1, 1, 30)",
                new Dictionary<string, object> { ["ts"] = DaysAgoIso(days) });

        // AnomalyAlerts (90 d, ISO-"O" UTC)
        foreach (var days in new[] { 120, 5 })
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO AnomalyAlerts (Context, Metric, Value, Expected, Sigma, CreatedAtUtc) VALUES ('Unknown', 'm', 1, 1, 1, @ts)",
                new Dictionary<string, object> { ["ts"] = DaysAgoIso(days) });

        var deleted = await new RetentionService(_db).SweepAsync();

        Assert.Equal(3, deleted);
        Assert.Equal(1, await CountAsync("FancontrolTelemetry"));
        Assert.Equal(1, await CountAsync("PowerSnapshots"));
        Assert.Equal(1, await CountAsync("AnomalyAlerts"));
        Assert.Equal("new", await _db.ExecuteScalarAsync<string>("SELECT Mode FROM FancontrolTelemetry"));
    }

    [Fact]
    public async Task Unacknowledged_maintenance_alerts_survive_any_age()
    {
        foreach (var (acked, days) in new[] { (1, 200), (0, 200), (1, 5) })
            await _db.ExecuteNonQueryAsync(
                "INSERT INTO MaintenanceAlerts (Signature, Kind, Target, Message, Severity, CreatedAtUtc, Acknowledged) VALUES (@sig, 'DiskFailure', 't', 'm', 'Critical', @ts, @ack)",
                new Dictionary<string, object> { ["sig"] = Guid.NewGuid().ToString(), ["ts"] = DaysAgoIso(days), ["ack"] = acked });

        await new RetentionService(_db).SweepAsync();

        // Only the 200-day-old ACKNOWLEDGED alert is pruned; the unacknowledged disk-death
        // warning stays until the user acts on it, however old.
        Assert.Equal(2, await CountAsync("MaintenanceAlerts"));
        Assert.Equal(1, await _db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM MaintenanceAlerts WHERE Acknowledged = 0"));
    }

    [Fact]
    public async Task Learned_state_is_never_touched()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO PowerBaselines (Context, ProcessName, Count, MeanW, M2, EwmaW, LastUpdated) VALUES ('Unknown', 'x', 1, 1, 0, 1, @ts)",
            new Dictionary<string, object> { ["ts"] = DaysAgoIso(400) });
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO MetricBaselines (Context, Metric, SampleCount, Mean, M2) VALUES ('Unknown', 'm', 1, 1, 0)");

        await new RetentionService(_db).SweepAsync();

        Assert.Equal(1, await CountAsync("PowerBaselines"));
        Assert.Equal(1, await CountAsync("MetricBaselines"));
    }
}
