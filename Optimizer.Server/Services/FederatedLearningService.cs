using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;

namespace Optimizer.Server.Services;

public class FederatedLearningService : IFederatedLearningService
{
    private readonly OptimizerDbContext _db;

    /// <summary>
    /// Minimum number of distinct contributors required before a category baseline
    /// is returned via GetBaselinesAsync.
    /// This is a lightweight k-anonymity guard: prevents a single user's (noised)
    /// contribution from being directly visible through the aggregate endpoint.
    /// </summary>
    public const int MinContributorThreshold = 5;

    public FederatedLearningService(OptimizerDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public async Task SubmitAsync(Guid userId, IReadOnlyList<CategoryContribution> contributions)
    {
        if (contributions == null || contributions.Count == 0) return;

        foreach (var c in contributions)
        {
            // Validate incoming contribution bounds — the client should have clamped these,
            // but we enforce server-side as a defence-in-depth measure.
            if (string.IsNullOrWhiteSpace(c.Category) || c.Category.Length > 64)
                throw new ArgumentException($"Invalid category name: '{c.Category}'");
            if (c.AcceptanceRate < 0 || c.AcceptanceRate > 1)
                throw new ArgumentException($"AcceptanceRate must be in [0,1]; got {c.AcceptanceRate} for category '{c.Category}'");
            if (c.SampleWeight < 0)
                throw new ArgumentException($"SampleWeight must be ≥ 0; got {c.SampleWeight} for category '{c.Category}'");

            // Upsert: one contribution per (userId, category)
            var existing = await _db.FederatedContributions
                .FirstOrDefaultAsync(f => f.UserId == userId && f.Category == c.Category);

            if (existing != null)
            {
                existing.AcceptanceRate = c.AcceptanceRate;
                existing.SampleWeight   = c.SampleWeight;
                existing.SubmittedUtc   = DateTime.UtcNow;
            }
            else
            {
                _db.FederatedContributions.Add(new FederatedContribution
                {
                    UserId         = userId,
                    Category       = c.Category,
                    AcceptanceRate = c.AcceptanceRate,
                    SampleWeight   = c.SampleWeight,
                    SubmittedUtc   = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CommunityBaseline>> GetBaselinesAsync()
    {
        // Load all contributions and compute weighted averages per category in memory.
        // For the expected scale (optimizer app with modest server deployments) this is fine.
        var all = await _db.FederatedContributions
            .AsNoTracking()
            .ToListAsync();

        var baselines = new List<CommunityBaseline>();

        var byCategory = all.GroupBy(f => f.Category);
        foreach (var group in byCategory)
        {
            int contributorCount = group.Select(f => f.UserId).Distinct().Count();

            // k-anonymity: skip categories with too few distinct contributors
            if (contributorCount < MinContributorThreshold) continue;

            // Weighted average: sum(rate * weight) / sum(weight)
            double totalWeight    = group.Sum(f => (double)f.SampleWeight);
            double weightedRateSum = group.Sum(f => f.AcceptanceRate * f.SampleWeight);

            double communityRate = totalWeight > 0
                ? weightedRateSum / totalWeight
                : group.Average(f => f.AcceptanceRate);  // fallback: simple average if all weights are 0

            // Clamp to [0, 1] in case of floating-point drift
            communityRate = Math.Clamp(communityRate, 0.0, 1.0);

            baselines.Add(new CommunityBaseline(group.Key, communityRate, contributorCount));
        }

        return baselines.OrderBy(b => b.Category).ToList();
    }
}
