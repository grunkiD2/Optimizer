namespace Optimizer.WinUI.Services.Cloud;

public interface IOptimizerCloudClient
{
    string? ServerUrl { get; }
    bool IsAuthenticated { get; }
    string? CurrentUserEmail { get; }

    Task<bool> RequestMagicLinkAsync(string serverUrl, string email);
    Task<bool> VerifyMagicLinkAsync(string token);
    Task LogoutAsync();

    Task<SyncPullResult?> PullAsync(long cursor);
    Task<SyncPushResult?> PushAsync(IReadOnlyList<CloudSyncItem> items);
}

public record CloudSyncItem(string ItemType, string ItemId, string Payload, bool IsDeleted = false);
public record SyncPullResult(long Cursor, IReadOnlyList<CloudSyncItem> Items);
public record SyncPushResult(long ServerVersion);
