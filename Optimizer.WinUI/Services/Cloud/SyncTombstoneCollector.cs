using System.Text.Json;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Cloud;

/// <summary>
/// Thread-safe, file-persisted collector of tombstones for locally-deleted sync items.
/// Persisted so tombstones survive app restarts before the next successful push.
/// </summary>
public class SyncTombstoneCollector : ISyncTombstoneCollector
{
    private readonly string _filePath;
    private readonly List<TombstoneRecord> _records = [];
    private readonly object _lock = new();

    public SyncTombstoneCollector()
        : this(AppPaths.GetDataFile("sync-tombstones.json")) { }

    // Constructor overload for unit tests (injectable path)
    public SyncTombstoneCollector(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public void Record(string itemType, string itemId)
    {
        lock (_lock)
        {
            // Avoid duplicates — same type/id combination
            _records.RemoveAll(r => r.ItemType == itemType && r.ItemId == itemId);
            _records.Add(new TombstoneRecord(itemType, itemId, DateTime.UtcNow));
            Persist();
        }
    }

    public IReadOnlyList<TombstoneRecord> GetAndClear()
    {
        lock (_lock)
        {
            var snapshot = _records.ToList();
            _records.Clear();
            Persist();
            return snapshot;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<TombstoneRecord>>(json);
            if (loaded != null) _records.AddRange(loaded);
        }
        catch { /* corrupted file — start fresh */ }
    }

    private void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_records));
        }
        catch { }
    }
}
