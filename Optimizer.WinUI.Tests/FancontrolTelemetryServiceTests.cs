using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Data;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FancontrolTelemetryServiceTests : IAsyncLifetime
{
    private string _root = "";
    private string _dbPath = "";
    private DatabaseService _db = null!;

    // Verbatim brain telemetry line (2026-06-12 contract).
    private const string Line1 = """{"night":false,"cpu":{"l":7.9,"t":48,"w":77.9},"fcWatchdogOff":false,"hint":null,"pumpRpm":1732,"nightOverride":null,"demands":{"case":40,"rad":38},"load":false,"gpu":{"l":12,"t":39,"w":69.8,"mem":50},"ts":"2026-06-12T14:19:28.9673917+02:00","apps":[],"cool":35.2,"trim":0,"game":false,"lhmOk":true,"app":null,"alarm":false,"mode":"IDLE"}""";
    private const string Line2 = """{"night":false,"cpu":{"l":55,"t":61,"w":117.2},"pumpRpm":1745,"demands":{"case":54,"rad":45},"gpu":{"l":97,"t":62,"w":296.5,"mem":80},"ts":"2026-06-12T14:19:33.9673917+02:00","apps":["destiny2"],"cool":36.1,"game":true,"lhmOk":true,"app":"destiny2","alarm":false,"mode":"APP[destiny2]"}""";

    public async Task InitializeAsync()
    {
        _root = Directory.CreateTempSubdirectory("fctel").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "state", "telemetry"));
        _dbPath = Path.Combine(_root, "test.db");
        _db = new DatabaseService(_dbPath);
        await _db.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private FancontrolTelemetryService MakeService() => new(_db, Path.Combine(_root, "state"));

    [Fact]
    public void ParseTelemetryLine_maps_the_real_contract()
    {
        var row = FancontrolTelemetryService.ParseTelemetryLine(Line2);
        Assert.NotNull(row);
        Assert.Equal("2026-06-12T14:19:33.9673917+02:00", row!["Ts"]);
        Assert.Equal("APP[destiny2]", row["Mode"]);
        Assert.Equal(1, row["Game"]);
        Assert.Equal(117.2, row["CpuWatts"]);
        Assert.Equal(296.5, row["GpuWatts"]);
        Assert.Equal(36.1, row["Coolant"]);
        Assert.Equal(1745, row["PumpRpm"]);
        Assert.Equal(54, row["CaseDemand"]);
        Assert.Equal("destiny2", row["App"]);
        Assert.Null(FancontrolTelemetryService.ParseTelemetryLine("{broken"));
    }

    [Fact]
    public async Task Ingest_is_idempotent_and_cursor_skips_old_lines()
    {
        var file = Path.Combine(_root, "state", "telemetry", "2026-06-12.jsonl");
        await File.WriteAllLinesAsync(file, [Line1]);
        var svc = MakeService();

        Assert.Equal(1, await svc.IngestAsync());
        Assert.Equal(0, await svc.IngestAsync());          // re-run: nothing new

        await File.AppendAllLinesAsync(file, [Line2]);     // brain appended a tick
        Assert.Equal(1, await svc.IngestAsync());          // only the new line

        var count = await _db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM FancontrolTelemetry");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task History_returns_points_in_order_with_null_preservation()
    {
        var file = Path.Combine(_root, "state", "telemetry", $"{DateTime.Now:yyyy-MM-dd}.jsonl");
        var now = DateTimeOffset.Now;
        // One full line and one without coolant/pump (nulls must survive to the API).
        var l1 = Line1.Replace("2026-06-12T14:19:28.9673917+02:00", now.AddMinutes(-10).ToString("o"));
        var l2 = """{"cpu":{"l":1,"t":40,"w":50},"demands":{"case":40,"rad":38},"gpu":{"l":1,"t":35,"w":20},"ts":"__TS__","mode":"IDLE","night":false,"game":false,"alarm":false}"""
            .Replace("__TS__", now.AddMinutes(-5).ToString("o"));
        await File.WriteAllLinesAsync(file, [l1, l2]);

        var svc = MakeService();
        Assert.Equal(2, await svc.IngestAsync());

        var points = await svc.GetHistoryAsync(hours: 1);
        Assert.Equal(2, points.Count);
        Assert.True(string.CompareOrdinal(points[0].Ts, points[1].Ts) < 0);
        Assert.Equal(35.2, points[0].Coolant);
        Assert.Null(points[1].Coolant);    // missing in source → null, not 0
        Assert.Null(points[1].PumpRpm);
    }

    [Fact]
    public async Task History_downsamples_to_maxPoints()
    {
        var file = Path.Combine(_root, "state", "telemetry", $"{DateTime.Now:yyyy-MM-dd}.jsonl");
        var now = DateTimeOffset.Now;
        var lines = Enumerable.Range(0, 100).Select(i =>
            Line1.Replace("2026-06-12T14:19:28.9673917+02:00", now.AddSeconds(-500 + i * 5).ToString("o")));
        await File.WriteAllLinesAsync(file, lines);

        var svc = MakeService();
        Assert.Equal(100, await svc.IngestAsync());

        var points = await svc.GetHistoryAsync(hours: 1, maxPoints: 20);
        Assert.InRange(points.Count, 10, 25);              // ~stride-sampled
        Assert.Equal(now.AddSeconds(-5).ToString("o"), points[^1].Ts); // newest point kept exactly
    }
}
