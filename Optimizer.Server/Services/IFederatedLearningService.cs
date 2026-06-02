namespace Optimizer.Server.Services;

/// <summary>
/// Represents one differentially-private category contribution from a client.
/// The client must apply Laplace noise before submitting — the server never sees raw data.
/// </summary>
public record CategoryContribution(
    /// <summary>Category name (e.g. "Performance", "Storage").</summary>
    string Category,
    /// <summary>DP-noised acceptance rate in [0,1].</summary>
    double AcceptanceRate,
    /// <summary>DP-noised interaction count ≥ 0. Used as weight in federated averaging.</summary>
    int SampleWeight);

/// <summary>
/// Aggregated community baseline for one category, exposed to clients.
/// Only returned when enough distinct users have contributed (k-anonymity threshold).
/// </summary>
public record CommunityBaseline(
    string Category,
    /// <summary>Weighted average of all contributors' acceptance rates.</summary>
    double CommunityAcceptanceRate,
    /// <summary>Number of distinct users who contributed to this category.</summary>
    int ContributorCount);

/// <summary>
/// Lightweight federated-averaging scaffold.
///
/// Architecture note: this is intentionally minimal — it performs plain federated averaging
/// with differential privacy enforced client-side. It is NOT a production FL system
/// (no secure aggregation, no Byzantine robustness, no gradient compression).
/// The goal is to expose community-informed recommendation defaults while ensuring
/// no individual's raw data ever leaves their device.
/// </summary>
public interface IFederatedLearningService
{
    /// <summary>
    /// Upserts a user's latest DP-noised contributions (one per category).
    /// Replaces any previous submission from the same user for each category.
    /// </summary>
    Task SubmitAsync(Guid userId, IReadOnlyList<CategoryContribution> contributions);

    /// <summary>
    /// Returns community baselines: weighted averages across all contributors, per category.
    /// Categories with fewer than the k-anonymity threshold of distinct contributors are omitted
    /// to prevent individual inference.
    /// </summary>
    Task<IReadOnlyList<CommunityBaseline>> GetBaselinesAsync();
}
