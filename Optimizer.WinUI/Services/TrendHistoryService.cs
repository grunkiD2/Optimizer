using System.Text.Json;
using System.Text.Json.Serialization;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Appends one sample per day to trend-history.json.
/// Caps the store at 365 entries per drive/disk to bound file growth.
/// Thread-safe: all file I/O is serialised through <c>_gate</c>.
/// </summary>
public class TrendHistoryService : ITrendHistoryService
{
    private static readonly string DataPath = AppPaths.GetDataFile("trend-history.json");
    private const int MaxDaysRetained = 365;

    private readonly IDiskHealthService _diskHealth;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // In-memory mirror
    private TrendHistoryStore _store = new();
    private bool _loaded;

    public TrendHistoryService(IDiskHealthService diskHealth)
    {
        _diskHealth = diskHealth;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task RecordSampleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            EnsureLoaded();
            var today = DateTime.Today;

            // ── Drive free space ──────────────────────────────────────────────
            foreach (var drive in DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                var letter = drive.Name.TrimEnd('\\', ':').ToUpperInvariant();
                if (!_store.DriveHistory.TryGetValue(letter, out var list))
                {
                    list = [];
                    _store.DriveHistory[letter] = list;
                }

                Upsert(list, today, new DriveSample
                {
                    DateTicks  = today.Ticks,
                    FreeBytes  = drive.AvailableFreeSpace,
                    TotalBytes = drive.TotalSize
                });
                Trim(list, MaxDaysRetained);
            }

            // ── SMART disk data ───────────────────────────────────────────────
            try
            {
                var disks = await _diskHealth.GetDiskHealthAsync();
                foreach (var disk in disks)
                {
                    var key = NormaliseSerial(disk.SerialNumber);
                    if (string.IsNullOrEmpty(key)) continue;

                    if (!_store.DiskHistory.TryGetValue(key, out var list))
                    {
                        list = [];
                        _store.DiskHistory[key] = list;
                    }

                    Upsert(list, today, new DiskSample
                    {
                        DateTicks       = today.Ticks,
                        WearPercent     = disk.WearPercentage ?? 0,
                        TemperatureC    = disk.TemperatureCelsius ?? 0,
                        PowerOnHours    = (int)Math.Min(int.MaxValue, disk.PowerOnHours ?? 0),
                        IsPredictedFail = disk.IsPredictedToFail
                    });
                    Trim(list, MaxDaysRetained);
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error("TrendHistoryService: SMART data collection failed", ex);
            }

            Save();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<(DateTime Date, long FreeBytes)> GetDriveFreeHistory(string driveLetter)
    {
        _gate.Wait();
        try
        {
            EnsureLoaded();
            var letter = driveLetter.TrimEnd('\\', ':').ToUpperInvariant();
            if (!_store.DriveHistory.TryGetValue(letter, out var list))
                return [];

            return list
                .Select(s => (new DateTime(s.DateTicks), s.FreeBytes))
                .OrderBy(t => t.Item1)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public IReadOnlyList<(DateTime Date, int WearPercent)> GetDiskWearHistory(string serial)
    {
        _gate.Wait();
        try
        {
            EnsureLoaded();
            var key = NormaliseSerial(serial);
            if (!_store.DiskHistory.TryGetValue(key, out var list))
                return [];

            return list
                .Select(s => (new DateTime(s.DateTicks), s.WearPercent))
                .OrderBy(t => t.Item1)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(DataPath)) return;
            var json = File.ReadAllText(DataPath);
            _store = JsonSerializer.Deserialize<TrendHistoryStore>(json) ?? new TrendHistoryStore();
        }
        catch (Exception ex)
        {
            EngineLog.Error("TrendHistoryService: failed to load history", ex);
            _store = new TrendHistoryStore();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath)!);
            File.WriteAllText(DataPath, JsonSerializer.Serialize(_store));
        }
        catch (Exception ex)
        {
            EngineLog.Error("TrendHistoryService: failed to save history", ex);
        }
    }

    /// <summary>
    /// Inserts or replaces a sample for the same calendar day (dedupe by date).
    /// </summary>
    private static void Upsert<T>(List<T> list, DateTime day, T sample) where T : IHasTicks
    {
        var idx = list.FindIndex(s => new DateTime(s.DateTicks).Date == day.Date);
        if (idx >= 0) list[idx] = sample;
        else          list.Add(sample);
    }

    private static void Trim<T>(List<T> list, int maxCount)
    {
        while (list.Count > maxCount)
            list.RemoveAt(0);
    }

    private static string NormaliseSerial(string serial) =>
        serial.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal);
}

// ── JSON model ────────────────────────────────────────────────────────────────

internal interface IHasTicks
{
    long DateTicks { get; }
}

internal sealed class TrendHistoryStore
{
    [JsonPropertyName("drives")]
    public Dictionary<string, List<DriveSample>> DriveHistory { get; set; } = [];

    [JsonPropertyName("disks")]
    public Dictionary<string, List<DiskSample>>  DiskHistory  { get; set; } = [];
}

internal sealed record DriveSample : IHasTicks
{
    [JsonPropertyName("t")]  public long DateTicks  { get; init; }
    [JsonPropertyName("f")]  public long FreeBytes  { get; init; }
    [JsonPropertyName("total")] public long TotalBytes { get; init; }
}

internal sealed record DiskSample : IHasTicks
{
    [JsonPropertyName("t")]    public long DateTicks       { get; init; }
    [JsonPropertyName("w")]    public int  WearPercent     { get; init; }
    [JsonPropertyName("temp")] public int  TemperatureC    { get; init; }
    [JsonPropertyName("poh")]  public int  PowerOnHours    { get; init; }
    [JsonPropertyName("fail")] public bool IsPredictedFail { get; init; }
}
