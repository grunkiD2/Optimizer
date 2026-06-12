using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services;

public interface IFancontrolTelemetryService
{
    /// <summary>Ingest new telemetry lines since the last run. Returns rows inserted.</summary>
    Task<int> IngestAsync(CancellationToken ct = default);

    /// <summary>Downsampled history for the last N hours (at most maxPoints rows, newest last).</summary>
    Task<List<FancontrolHistoryPoint>> GetHistoryAsync(double hours, int maxPoints = 300, CancellationToken ct = default);
}

public record FancontrolHistoryPoint(
    string Ts, string Mode, double? Coolant, double? CpuWatts, double? GpuWatts,
    double? CpuTemp, double? GpuTemp, int? PumpRpm, int? CaseDemand, int? RadDemand);

/// <summary>
/// Ingests the Fancontrol brain's 5 s telemetry archive (state\telemetry\YYYY-MM-DD.jsonl,
/// READ-ONLY — docs/MACHINE-OWNERSHIP.md) into SQLite for trend surfaces and the
/// /api/fancontrol/history endpoint. Runs as a hosted timer every 5 minutes; the Ts primary
/// key + INSERT OR IGNORE makes every run idempotent, and a cheap timestamp prefilter skips
/// already-ingested lines before any JSON parsing (the analyze.ps1 lesson).
/// </summary>
public class FancontrolTelemetryService : IFancontrolTelemetryService, IHostedService, IDisposable
{
    private static readonly TimeSpan IngestInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(45); // let the host settle first

    private readonly DatabaseService _db;
    private readonly string _telemetryDir;
    private Timer? _timer;
    private int _running; // re-entrancy guard (Interlocked)

    public FancontrolTelemetryService(DatabaseService db, string stateDir)
    {
        _db = db;
        var dir = stateDir?.Trim().TrimEnd('\\', '/') ?? "";
        _telemetryDir = dir.Length == 0 ? "" : Path.Combine(dir, "telemetry");
    }

    public bool IsConfigured => _telemetryDir.Length > 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsConfigured)
            _timer = new Timer(async _ => await IngestSafeAsync(), null, StartupDelay, IngestInterval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task IngestSafeAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try
        {
            var n = await IngestAsync();
            if (n > 0) EngineLog.Write($"[FancontrolTelemetry] ingested {n} new telemetry rows");
        }
        catch (Exception ex) { EngineLog.Error("[FancontrolTelemetry] ingest failed", ex); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    public async Task<int> IngestAsync(CancellationToken ct = default)
    {
        if (!IsConfigured || !Directory.Exists(_telemetryDir)) return 0;

        var cursor = await _db.ExecuteScalarAsync<string>("SELECT MAX(Ts) FROM FancontrolTelemetry") ?? "";
        var cursorDate = cursor.Length >= 10 ? cursor[..10] : "";

        var inserted = 0;
        foreach (var file in Directory.GetFiles(_telemetryDir, "????-??-??.jsonl").OrderBy(f => f))
        {
            ct.ThrowIfCancellationRequested();
            // File names are the brain's local dates — skip whole files older than the cursor's date.
            var fileDate = Path.GetFileNameWithoutExtension(file);
            if (cursorDate.Length > 0 && string.CompareOrdinal(fileDate, cursorDate) < 0) continue;

            string[] lines;
            try
            {
                // The brain appends continuously — share-tolerant read.
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                lines = (await sr.ReadToEndAsync(ct)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            }
            catch { continue; } // transient share violation — next run gets it

            var rows = new List<Dictionary<string, object>>();
            foreach (var line in lines)
            {
                // Cheap prefilter before JSON parse: lines carry "ts":"<ISO>" — already-ingested
                // timestamps sort lexicographically below the cursor (constant local offset).
                var ts = ExtractTs(line);
                if (ts.Length == 0 || (cursor.Length > 0 && string.CompareOrdinal(ts, cursor) <= 0)) continue;
                if (ParseTelemetryLine(line) is { } row) rows.Add(row);
            }
            if (rows.Count == 0) continue;

            await _db.RunInTransactionAsync(async batch =>
            {
                foreach (var row in rows)
                    await batch.ExecuteNonQueryAsync(
                        """
                        INSERT OR IGNORE INTO FancontrolTelemetry
                        (Ts, Mode, Night, Game, Alarm, CpuLoad, CpuTemp, CpuWatts, GpuLoad, GpuTemp, GpuWatts, GpuMem, Coolant, PumpRpm, CaseDemand, RadDemand, App)
                        VALUES (@Ts, @Mode, @Night, @Game, @Alarm, @CpuLoad, @CpuTemp, @CpuWatts, @GpuLoad, @GpuTemp, @GpuWatts, @GpuMem, @Coolant, @PumpRpm, @CaseDemand, @RadDemand, @App)
                        """, row);
            });
            inserted += rows.Count;
        }
        return inserted;
    }

    internal static string ExtractTs(string line)
    {
        var i = line.IndexOf("\"ts\":\"", StringComparison.Ordinal);
        if (i < 0) return "";
        var start = i + 6;
        var end = line.IndexOf('"', start);
        return end > start ? line[start..end] : "";
    }

    /// <summary>Parses one brain telemetry line into DB parameters; null when malformed.</summary>
    internal static Dictionary<string, object>? ParseTelemetryLine(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            string ts = GetString(r, "ts");
            if (ts.Length == 0) return null;
            double? cpuL = null, cpuT = null, cpuW = null, gpuL = null, gpuT = null, gpuW = null, gpuMem = null;
            if (r.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Object)
            { cpuL = GetDouble(cpu, "l"); cpuT = GetDouble(cpu, "t"); cpuW = GetDouble(cpu, "w"); }
            if (r.TryGetProperty("gpu", out var gpu) && gpu.ValueKind == JsonValueKind.Object)
            { gpuL = GetDouble(gpu, "l"); gpuT = GetDouble(gpu, "t"); gpuW = GetDouble(gpu, "w"); gpuMem = GetDouble(gpu, "mem"); }
            int? demCase = null, demRad = null;
            if (r.TryGetProperty("demands", out var dem) && dem.ValueKind == JsonValueKind.Object)
            { demCase = (int?)GetDouble(dem, "case"); demRad = (int?)GetDouble(dem, "rad"); }

            // NB: DatabaseService prepends "@" to parameter names — keys here are bare.
            return new Dictionary<string, object>
            {
                ["Ts"] = ts,
                ["Mode"] = GetString(r, "mode"),
                ["Night"] = GetBool(r, "night") ? 1 : 0,
                ["Game"] = GetBool(r, "game") ? 1 : 0,
                ["Alarm"] = GetBool(r, "alarm") ? 1 : 0,
                ["CpuLoad"] = (object?)cpuL ?? DBNull.Value,
                ["CpuTemp"] = (object?)cpuT ?? DBNull.Value,
                ["CpuWatts"] = (object?)cpuW ?? DBNull.Value,
                ["GpuLoad"] = (object?)gpuL ?? DBNull.Value,
                ["GpuTemp"] = (object?)gpuT ?? DBNull.Value,
                ["GpuWatts"] = (object?)gpuW ?? DBNull.Value,
                ["GpuMem"] = (object?)gpuMem ?? DBNull.Value,
                ["Coolant"] = (object?)GetDouble(r, "cool") ?? DBNull.Value,
                ["PumpRpm"] = (object?)(int?)GetDouble(r, "pumpRpm") ?? DBNull.Value,
                ["CaseDemand"] = (object?)demCase ?? DBNull.Value,
                ["RadDemand"] = (object?)demRad ?? DBNull.Value,
                ["App"] = (object?)GetStringOrNull(r, "app") ?? DBNull.Value,
            };
        }
        catch { return null; }
    }

    public async Task<List<FancontrolHistoryPoint>> GetHistoryAsync(double hours, int maxPoints = 300, CancellationToken ct = default)
    {
        hours = Math.Clamp(hours, 0.1, 24 * 14);
        maxPoints = Math.Clamp(maxPoints, 10, 2000);
        var cutoff = DateTimeOffset.Now.AddHours(-hours).ToString("yyyy-MM-ddTHH:mm:ss");
        var rows = await _db.ExecuteQueryAsync(
            "SELECT Ts, Mode, Coolant, CpuWatts, GpuWatts, CpuTemp, GpuTemp, PumpRpm, CaseDemand, RadDemand FROM FancontrolTelemetry WHERE Ts >= @cutoff ORDER BY Ts",
            new Dictionary<string, object> { ["cutoff"] = cutoff });

        // Stride-downsample to maxPoints (keep the newest point exactly).
        var stride = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)maxPoints));
        var points = new List<FancontrolHistoryPoint>(Math.Min(rows.Count, maxPoints) + 1);
        for (var i = 0; i < rows.Count; i += stride)
            points.Add(ToPoint(rows[i]));
        if (rows.Count > 0 && (rows.Count - 1) % stride != 0)
            points.Add(ToPoint(rows[^1]));
        return points;
    }

    // NULL columns must stay null in the API (DbRow.GetDouble maps null to 0 — misleading in trends).
    private static double? D(DbRow row, string col) => row[col] switch { double d => d, long l => l, _ => null };
    private static int? I(DbRow row, string col) => row[col] switch { long l => (int)l, double d => (int)d, _ => null };

    private static FancontrolHistoryPoint ToPoint(DbRow row) => new(
        Ts: row.GetString("Ts"),
        Mode: row.GetString("Mode"),
        Coolant: D(row, "Coolant"),
        CpuWatts: D(row, "CpuWatts"),
        GpuWatts: D(row, "GpuWatts"),
        CpuTemp: D(row, "CpuTemp"),
        GpuTemp: D(row, "GpuTemp"),
        PumpRpm: I(row, "PumpRpm"),
        CaseDemand: I(row, "CaseDemand"),
        RadDemand: I(row, "RadDemand"));

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
    private static string? GetStringOrNull(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool GetBool(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    private static double? GetDouble(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
