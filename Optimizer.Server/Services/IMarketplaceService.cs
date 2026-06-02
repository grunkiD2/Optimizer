using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public interface IMarketplaceService
{
    Task<MarketplaceBrowseResponse> BrowseAsync(string? category, string? search, string sortBy, int page, int pageSize);
    Task<MarketplaceListingDto?> GetByPublicIdAsync(string publicId);
    Task<bool> IncrementDownloadAsync(string publicId);
    Task<SubmitListingResponse> SubmitAsync(Guid userId, SubmitListingRequest request);
    Task<RatingDto?> SubmitRatingAsync(string publicId, Guid userId, SubmitRatingRequest request);
    Task<bool> ReportAsync(string publicId, Guid reporterId, ReportListingRequest request);
}
