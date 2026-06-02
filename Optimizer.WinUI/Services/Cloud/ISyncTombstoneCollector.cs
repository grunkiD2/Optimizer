namespace Optimizer.WinUI.Services.Cloud;

public record TombstoneRecord(string ItemType, string ItemId, DateTime DeletedAtUtc);

/// <summary>
/// Collects locally-deleted items so the cloud sync orchestrator can push tombstones.
/// Implemented as a singleton so both service layers (ProfileService, HistoryService)
/// and the orchestrator share the same queue without a circular dependency.
/// </summary>
public interface ISyncTombstoneCollector
{
    void Record(string itemType, string itemId);
    IReadOnlyList<TombstoneRecord> GetAndClear();
}
