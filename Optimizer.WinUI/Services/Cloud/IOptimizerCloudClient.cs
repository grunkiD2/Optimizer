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

    // Plugin marketplace
    Task<RemotePluginBrowseResult?> BrowsePluginsAsync(string? category, string? search, string? sort, int page, int pageSize);
    Task<RemotePluginDetail?> GetPluginDetailAsync(string pluginId);
    Task<bool> IncrementPluginDownloadAsync(string pluginId);
    Task<bool> SubmitPluginAsync(string manifestYaml);

    // Event forwarding (best-effort, called from EventBus background queue)
    Task ForwardEventAsync(string type, string title, string detail, IReadOnlyDictionary<string, string>? data);

    // Federated Learning scaffold (opt-in, DP-protected)
    // Uploads the user's differentially-private model statistics.
    // The caller is responsible for applying DP noise before invoking this method.
    Task<bool> ContributeFederatedAsync(IReadOnlyList<FederatedCategoryContribution> contributions);

    // Downloads community-aggregated baselines (only categories meeting the k-anonymity threshold).
    Task<IReadOnlyList<FederatedCommunityBaseline>?> GetCommunityBaselinesAsync();
}

/// <summary>One category's DP-noised contribution to upload to the federated server.</summary>
public record FederatedCategoryContribution(string Category, double AcceptanceRate, int SampleWeight);

/// <summary>Community-aggregated acceptance rate baseline for one category.</summary>
public record FederatedCommunityBaseline(string Category, double CommunityAcceptanceRate, int ContributorCount);

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

// Plugin marketplace DTOs
public record RemotePluginListing(
    string PluginId,
    string Name,
    string AuthorDisplayName,
    string Description,
    string Category,
    int Downloads,
    double AverageRating,
    int RatingCount,
    bool Verified);

public record RemotePluginBrowseResult(int Total, int Page, int PageSize, IReadOnlyList<RemotePluginListing> Listings);

public record RemotePluginDetail(
    string PluginId,
    string Name,
    string AuthorDisplayName,
    string Description,
    string Category,
    string ManifestYaml,
    string? Signature,
    bool Verified,
    int Downloads,
    double AverageRating,
    int RatingCount);
