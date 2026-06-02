using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IMarketplaceService
{
    Task<IReadOnlyList<MarketplaceEntry>> LoadCatalogAsync(bool includeRemote = false);
    Task<bool> InstallAsync(MarketplaceEntry entry);
    Task RateAsync(string id, int rating);
    /// <summary>Serializes <paramref name="entry"/> to a submission JSON the user can email/upload. Returns saved file path.</summary>
    Task<string> GenerateSubmissionAsync(MarketplaceEntry entry);
    IReadOnlyDictionary<string, int> GetUserRatings();
}
