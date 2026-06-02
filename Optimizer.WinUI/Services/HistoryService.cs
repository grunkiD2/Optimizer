using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Cloud;

namespace Optimizer.WinUI.Services;

public class HistoryService : IHistoryService
{
    private readonly List<HistoryEntry> _entries = [];
    private static readonly string FilePath = AppPaths.GetDataFile("change-history.json");
    private readonly ISyncTombstoneCollector? _tombstones;

    public HistoryService() { }

    public HistoryService(ISyncTombstoneCollector tombstones)
    {
        _tombstones = tombstones;
    }

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void Load()
    {
        _entries.Clear();
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var loaded = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
            if (loaded != null) _entries.AddRange(loaded);
        }
        catch
        {
            // corrupted file — start fresh
        }
    }

    public void RecordApplied(string optimizationId, string title, string category, bool reversible)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.Applied,
            IsReversible = reversible,
            TimestampUtc = DateTime.UtcNow
        });
        Save();
    }

    public void RecordOneTime(string optimizationId, string title, string category, string resultText)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.OneTime,
            IsReversible = false,
            ResultText = resultText,
            TimestampUtc = DateTime.UtcNow
        });
        Save();
    }

    public void RecordUndone(string optimizationId, string title, string category)
    {
        _entries.Insert(0, new HistoryEntry
        {
            OptimizationId = optimizationId,
            OptimizationTitle = title,
            Category = category,
            Action = HistoryAction.Undone,
            IsReversible = false,
            TimestampUtc = DateTime.UtcNow
        });

        foreach (var e in _entries.Where(e =>
            e.OptimizationId == optimizationId && e.Action == HistoryAction.Applied && !e.IsUndone))
        {
            e.IsUndone = true;
        }
        Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    /// <summary>Insert or replace an entry by its Id (used by cloud sync to apply remote history items).</summary>
    public void UpsertEntry(HistoryEntry entry)
    {
        var idx = _entries.FindIndex(e => e.Id == entry.Id);
        if (idx >= 0)
            _entries[idx] = entry;
        else
            _entries.Insert(0, entry);  // newest-first ordering
        Save();
    }

    /// <summary>Delete a history entry by Id; records a tombstone for cloud sync if configured.</summary>
    public bool DeleteEntry(string id)
    {
        var removed = _entries.RemoveAll(e => e.Id == id) > 0;
        if (removed)
        {
            Save();
            _tombstones?.Record("history", id);
        }
        return removed;
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
