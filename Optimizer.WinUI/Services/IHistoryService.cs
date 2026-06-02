using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IHistoryService
{
    IReadOnlyList<HistoryEntry> Entries { get; }
    void Load();
    void RecordApplied(string optimizationId, string title, string category, bool reversible);
    void RecordOneTime(string optimizationId, string title, string category, string resultText);
    void RecordUndone(string optimizationId, string title, string category);
    void Clear();
}
