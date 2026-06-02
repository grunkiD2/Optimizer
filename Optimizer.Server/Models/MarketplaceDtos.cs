namespace Optimizer.Server.Models;

public record MarketplaceListingDto(
    Guid Id,
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

public record MarketplaceBrowseResponse(
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<MarketplaceListingDto> Listings);

public record SubmitListingRequest(
    string Name,
    string Description,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Optimizations);

public record SubmitListingResponse(Guid Id, ListingStatusDto Status);

public enum ListingStatusDto { Pending, Approved, Rejected }

public record SubmitRatingRequest(int Stars, string? Comment);
public record RatingDto(int Stars, string? Comment, DateTime UpdatedAtUtc);

public record ReportListingRequest(string Reason, string? Comment);
