using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class HistoryService
{
    private readonly List<HistoryEntry> _entries = [];
    private static readonly string FilePath = AppPaths.GetDataFile("change-history.json");

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

    private void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
