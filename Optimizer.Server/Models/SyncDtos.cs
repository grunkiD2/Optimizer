namespace Optimizer.Server.Models;

public record SyncPullResponse(
    long Cursor,
    long ServerVersion,
    IReadOnlyList<SyncItemDto> Items);

public record SyncItemDto(
    string ItemType,
    string ItemId,
    long Version,
    DateTime UpdatedAtUtc,
    string Payload,
    bool IsDeleted);

public record SyncPushRequest(IReadOnlyList<SyncPushItem> Items);

public record SyncPushItem(
    string ItemType,
    string ItemId,
    string Payload,
    bool IsDeleted = false);

public record SyncPushResponse(
    long ServerVersion,
    IReadOnlyList<SyncPushResult> Results);

public record SyncPushResult(string ItemType, string ItemId, long Version);
