using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public interface IPluginMarketplaceService
{
    Task<PluginBrowseResponse> BrowseAsync(string? category, string? search, string sortBy, int page, int pageSize);
    Task<PluginDetailDto?> GetByPluginIdAsync(string pluginId);
    Task<bool> IncrementDownloadAsync(string pluginId);
    Task<SubmitPluginResponse> SubmitAsync(Guid userId, SubmitPluginRequest request);
    Task<RatingDto?> SubmitRatingAsync(string pluginId, Guid userId, SubmitRatingRequest request);
}
