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

    // Marketplace
    Task<RemoteMarketplaceBrowseResult?> BrowseMarketplaceAsync(string? category, string? search, string? sort, int page, int pageSize);
    Task<RemoteMarketplaceListing?> GetMarketplaceListingAsync(string publicId);
    Task<bool> IncrementMarketplaceDownloadAsync(string publicId);
    Task<bool> SubmitMarketplaceListingAsync(MarketplaceSubmission submission);
    Task<bool> RateMarketplaceListingAsync(string publicId, int stars, string? comment);
}

public record CloudSyncItem(string ItemType, string ItemId, string Payload, bool IsDeleted = false);
public record SyncPullResult(long Cursor, IReadOnlyList<CloudSyncItem> Items);
public record SyncPushResult(long ServerVersion);

// Marketplace DTOs
public record RemoteMarketplaceBrowseResult(int Total, int Page, int PageSize, IReadOnlyList<RemoteMarketplaceListing> Listings);
public record RemoteMarketplaceListing(
    string PublicId,
    string Name,
    string AuthorDisplayName,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Optimizations,
    int Downloads,
    double AverageRating,
    int RatingCount,
    bool Verified,
    bool Featured);
public record MarketplaceSubmission(string Name, string Description, string Category, IReadOnlyList<string> Tags, IReadOnlyList<string> Optimizations);
