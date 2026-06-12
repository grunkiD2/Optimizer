using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Power;

public record PowerDrainerRow(string Name, int InstanceCount, double EstimatedWatts, double CpuShare,
    double? BaselineW, double? ZScore, string Drift, bool Excluded);

public record PowerDriftEvent(long Id, string Ts, string Context, string ProcessName,
    double ObservedW, double BaselineW, double ZScore, string Classification);

public interface IPowerInsightsService
{
    bool Enabled { get; }
    PowerAttributionSnapshot? LatestSnapshot { get; }
    string LatestContext { get; }
    IReadOnlyList<PowerDrainerRow> GetTopDrainers(int count = 15);
    Task<List<PowerDriftEvent>> GetRecentDriftAsync(double hours = 24, int limit = 50);
}

/// <summary>
/// Per-Process Power Intelligence (docs/POWER-INSIGHTS.md) — the Adaptive Drift Surfacing
/// loop: every 30 s attribute measured package watts to processes, learn per-(context,
/// process) Welford baselines with 72 h half-life decay, classify with a modified-z test,
/// and surface drift. READ-ONLY by contract: suggestions only, never kills/re-prioritizes/
/// writes anything outside its own SQLite tables. Off by default (AppSettings.PpiEnabled).
/// </summary>
public class PowerInsightsService : IPowerInsightsService, IHostedService, IDisposable
{
    private static readonly TimeSpan Cadence = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DriftResurfaceWindow = TimeSpan.FromHours(4);
    private const double MinWattsToTrack = 0.5;   // ignore noise-floor processes
    private const int TopNTracked = 15;

    private readonly IPowerAttributionService _attribution;
    private readonly DatabaseService _db;
    private readonly ISettingsService _settings;
    private readonly IContextDetectionService _context;

    private readonly Dictionary<(string Context, string Process), PowerBaseline> _baselines = new();
    private readonly Dictionary<(string Context, string Process, string Class), (DateTimeOffset At, double Watts)> _lastSurfaced = new();
    private readonly object _gate = new();
    private List<PowerDrainerRow> _topDrainers = [];
    private Timer? _timer;
    private int _running;
    private bool _baselinesLoaded;

    public PowerInsightsService(IPowerAttributionService attribution, DatabaseService db,
        ISettingsService settings, IContextDetectionService context)
    {
        _attribution = attribution;
        _db = db;
        _settings = settings;
        _context = context;
    }

    public bool Enabled => _settings.Settings.PpiEnabled;
    public PowerAttributionSnapshot? LatestSnapshot { get; private set; }
    public string LatestContext { get; private set; } = "Unknown";

    public IReadOnlyList<PowerDrainerRow> GetTopDrainers(int count = 15)
    {
        lock (_gate) return _topDrainers.Take(count).ToList();
    }

    public Task<List<PowerDriftEvent>> GetRecentDriftAsync(double hours = 24, int limit = 50)
        => QueryDriftAsync(hours, limit);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(async _ => await TickSafeAsync(), null, StartupDelay, Cadence);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task TickSafeAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        try { await TickAsync(); }
        catch (Exception ex) { EngineLog.Error("[PowerInsights] tick failed", ex); }
        finally { Interlocked.Exchange(ref _running, 0); }
    }

    private async Task TickAsync()
    {
        if (!Enabled) return;
        var snapshot = _attribution.Sample();   // first call primes counters → null
        if (snapshot == null) return;
        LatestSnapshot = snapshot;

        if (!_baselinesLoaded) { await LoadBaselinesAsync(); _baselinesLoaded = true; }

        string context;
        try { context = await _context.DetectContextAsync() ?? "Unknown"; }
        catch { context = "Unknown"; }
        LatestContext = context;

        var exclusions = CompileExclusions(_settings.Settings.PpiProcessExclusions);
        var zThreshold = Math.Max(1.5, _settings.Settings.PpiDriftZThreshold);
        var halfLife = Math.Max(1, _settings.Settings.PpiBaselineHalfLifeHours);
        var now = snapshot.Timestamp;

        var rows = new List<PowerDrainerRow>();
        var dirty = new List<(string Context, string Process, PowerBaseline B)>();
        var snapshotRows = new List<Dictionary<string, object>>();

        foreach (var p in snapshot.Processes.Where(p => p.EstimatedWatts >= MinWattsToTrack).Take(TopNTracked))
        {
            var excluded = exclusions.Any(rx => rx.IsMatch(p.Name));
            var key = (context, p.Name);
            if (!_baselines.TryGetValue(key, out var baseline))
                _baselines[key] = baseline = new PowerBaseline();

            var cls = DriftDetector.Classify(baseline, p.EstimatedWatts, zThreshold);
            var z = DriftDetector.ZScore(baseline, p.EstimatedWatts);
            var baselineW = baseline.Count >= DriftDetector.MinSamplesBeforeClassify ? baseline.MeanW : (double?)null;

            if (!excluded && cls is DriftClass.Elevated or DriftClass.Anomalous)
                await SurfaceDriftAsync(now, context, p, baseline, z, cls);

            DriftDetector.Update(baseline, p.EstimatedWatts, now, halfLife);
            dirty.Add((context, p.Name, baseline));

            rows.Add(new PowerDrainerRow(p.Name, p.InstanceCount, p.EstimatedWatts, p.CpuShare,
                baselineW, baseline.Count >= DriftDetector.MinSamplesBeforeClassify ? z : null,
                excluded ? "excluded" : cls.ToString().ToLowerInvariant(), excluded));

            snapshotRows.Add(new Dictionary<string, object>
            {
                ["Ts"] = now.ToString("o"),
                ["Context"] = context,
                ["ProcessName"] = p.Name,
                ["AvgPowerW"] = p.EstimatedWatts,
                ["CpuShare"] = p.CpuShare,
                ["WindowSec"] = snapshot.WindowSeconds,
            });
        }

        lock (_gate) _topDrainers = rows;
        await PersistAsync(dirty, snapshotRows);
    }

    private async Task SurfaceDriftAsync(DateTimeOffset now, string context, ProcessPowerReading p,
        PowerBaseline baseline, double z, DriftClass cls)
    {
        // De-dup: same (process, context, class) stays quiet for 4 h unless magnitude grows ≥50%.
        var key = (context, p.Name, cls.ToString());
        if (_lastSurfaced.TryGetValue(key, out var last) &&
            now - last.At < DriftResurfaceWindow && p.EstimatedWatts < last.Watts * 1.5) return;
        _lastSurfaced[key] = (now, p.EstimatedWatts);

        await _db.ExecuteNonQueryAsync(
            "INSERT INTO PowerDriftEvents (Ts, Context, ProcessName, ObservedW, BaselineW, ZScore, Classification) VALUES (@Ts, @Context, @ProcessName, @ObservedW, @BaselineW, @ZScore, @Classification)",
            new Dictionary<string, object>
            {
                ["Ts"] = now.ToString("o"),
                ["Context"] = context,
                ["ProcessName"] = p.Name,
                ["ObservedW"] = p.EstimatedWatts,
                ["BaselineW"] = baseline.MeanW,
                ["ZScore"] = z,
                ["Classification"] = cls.ToString().ToLowerInvariant(),
            });

        if (_settings.Settings.PpiSuggestionsEnabled)
            EngineLog.Write($"[PowerInsights] {cls}: {p.Name} draws {p.EstimatedWatts:F1} W in {context} vs {baseline.MeanW:F1} W baseline (z={z:F1})");
    }

    private async Task PersistAsync(List<(string Context, string Process, PowerBaseline B)> dirty,
        List<Dictionary<string, object>> snapshotRows)
    {
        await _db.RunInTransactionAsync(async batch =>
        {
            foreach (var (ctx, proc, b) in dirty)
                await batch.ExecuteNonQueryAsync(
                    """
                    INSERT INTO PowerBaselines (Context, ProcessName, Count, MeanW, M2, EwmaW, LastUpdated)
                    VALUES (@Context, @ProcessName, @Count, @MeanW, @M2, @EwmaW, @LastUpdated)
                    ON CONFLICT(Context, ProcessName) DO UPDATE SET
                      Count=excluded.Count, MeanW=excluded.MeanW, M2=excluded.M2,
                      EwmaW=excluded.EwmaW, LastUpdated=excluded.LastUpdated
                    """,
                    new Dictionary<string, object>
                    {
                        ["Context"] = ctx,
                        ["ProcessName"] = proc,
                        ["Count"] = b.Count,
                        ["MeanW"] = b.MeanW,
                        ["M2"] = b.M2,
                        ["EwmaW"] = b.EwmaW,
                        ["LastUpdated"] = b.LastUpdated.ToString("o"),
                    });
            foreach (var row in snapshotRows)
                await batch.ExecuteNonQueryAsync(
                    "INSERT INTO PowerSnapshots (Ts, Context, ProcessName, AvgPowerW, CpuShare, WindowSec) VALUES (@Ts, @Context, @ProcessName, @AvgPowerW, @CpuShare, @WindowSec)", row);
        });
    }

    private async Task LoadBaselinesAsync()
    {
        var rows = await _db.ExecuteQueryAsync("SELECT Context, ProcessName, Count, MeanW, M2, EwmaW, LastUpdated FROM PowerBaselines");
        foreach (var r in rows)
        {
            var b = new PowerBaseline
            {
                Count = r.GetDouble("Count"),
                MeanW = r.GetDouble("MeanW"),
                M2 = r.GetDouble("M2"),
                EwmaW = r.GetDouble("EwmaW"),
            };
            if (DateTimeOffset.TryParse(r.GetString("LastUpdated"), out var ts)) b.LastUpdated = ts;
            _baselines[(r.GetString("Context"), r.GetString("ProcessName"))] = b;
        }
        if (rows.Count > 0) EngineLog.Write($"[PowerInsights] loaded {rows.Count} persisted baselines");
    }

    private async Task<List<PowerDriftEvent>> QueryDriftAsync(double hours, int limit)
    {
        var cutoff = DateTimeOffset.Now.AddHours(-Math.Clamp(hours, 0.1, 24 * 14)).ToString("o");
        var rows = await _db.ExecuteQueryAsync(
            "SELECT Id, Ts, Context, ProcessName, ObservedW, BaselineW, ZScore, Classification FROM PowerDriftEvents WHERE Ts >= @cutoff ORDER BY Ts DESC LIMIT @limit",
            new Dictionary<string, object> { ["cutoff"] = cutoff, ["limit"] = Math.Clamp(limit, 1, 500) });
        return rows.Select(r => new PowerDriftEvent(
            r.GetLong("Id"), r.GetString("Ts"), r.GetString("Context"), r.GetString("ProcessName"),
            r.GetDouble("ObservedW"), r.GetDouble("BaselineW"), r.GetDouble("ZScore"), r.GetString("Classification"))).ToList();
    }

    internal static List<Regex> CompileExclusions(IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        foreach (var p in patterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { list.Add(new Regex(p.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))); }
            catch { /* invalid user regex — skip */ }
        }
        return list;
    }
}
