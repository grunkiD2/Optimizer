using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public class PluginMarketplaceService : IPluginMarketplaceService
{
    private readonly OptimizerDbContext _db;
    private const int MaxManifestBytes = 32 * 1024;  // 32 KB

    public PluginMarketplaceService(OptimizerDbContext db) => _db = db;

    public async Task<PluginBrowseResponse> BrowseAsync(string? category, string? search, string sortBy, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.PluginListings.Where(l => l.Status == ListingStatus.Approved);

        if (!string.IsNullOrEmpty(category) && category != "All")
            query = query.Where(l => l.Category == category);

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLowerInvariant();
            query = query.Where(l =>
                l.Name.ToLower().Contains(s) ||
                l.Description.ToLower().Contains(s) ||
                l.PluginId.ToLower().Contains(s));
        }

        query = sortBy?.ToLowerInvariant() switch
        {
            "rating"  => query.OrderByDescending(l => l.AverageRating).ThenByDescending(l => l.RatingCount),
            "newest"  => query.OrderByDescending(l => l.CreatedAtUtc),
            "az"      => query.OrderBy(l => l.Name),
            _         => query.OrderByDescending(l => l.Verified).ThenByDescending(l => l.Downloads)
        };

        var total    = await query.CountAsync();
        var listings = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PluginBrowseResponse(total, page, pageSize, listings.Select(MapToDto).ToList());
    }

    public async Task<PluginDetailDto?> GetByPluginIdAsync(string pluginId)
    {
        var listing = await _db.PluginListings.FirstOrDefaultAsync(l =>
            l.PluginId == pluginId && l.Status == ListingStatus.Approved);
        return listing == null ? null : MapToDetail(listing);
    }

    public async Task<bool> IncrementDownloadAsync(string pluginId)
    {
        var listing = await _db.PluginListings.FirstOrDefaultAsync(l =>
            l.PluginId == pluginId && l.Status == ListingStatus.Approved);
        if (listing == null) return false;
        listing.Downloads++;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<SubmitPluginResponse> SubmitAsync(Guid userId, SubmitPluginRequest request)
    {
        // Server-side sanity validation
        if (string.IsNullOrWhiteSpace(request.ManifestYaml))
            throw new ArgumentException("ManifestYaml is required");

        var bytes = Encoding.UTF8.GetByteCount(request.ManifestYaml);
        if (bytes > MaxManifestBytes)
            throw new ArgumentException($"Manifest too large ({bytes} bytes, max {MaxManifestBytes})");

        // Lightweight content check: must contain required keys
        var yaml = request.ManifestYaml;
        foreach (var requiredKey in new[] { "manifest_version", "id:", "name:", "changes:" })
        {
            if (!yaml.Contains(requiredKey, StringComparison.Ordinal))
                throw new ArgumentException($"Manifest missing required field: {requiredKey.TrimEnd(':')}");
        }

        var user = await _db.Users.FindAsync(userId)
            ?? throw new InvalidOperationException("User not found");

        // Extract plugin id from YAML (simple line-based parse — full parse is client-side)
        var pluginId = ExtractYamlField(yaml, "id") ?? $"plugin-{Guid.NewGuid().ToString()[..8]}";
        var name     = ExtractYamlField(yaml, "name") ?? pluginId;
        var desc     = ExtractYamlField(yaml, "description") ?? "";
        var category = ExtractYamlField(yaml, "category") ?? "General";

        // Check for duplicate pluginId (pending or approved)
        if (await _db.PluginListings.AnyAsync(l => l.PluginId == pluginId && l.Status != ListingStatus.Rejected))
            throw new ArgumentException($"A plugin with id '{pluginId}' already exists or is pending review");

        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml))).ToLowerInvariant();

        var listing = new PluginListing
        {
            AuthorId          = userId,
            PluginId          = pluginId,
            Name              = name.Length > 80 ? name[..80] : name,
            AuthorDisplayName = user.DisplayName,
            Description       = desc.Length > 500 ? desc[..500] : desc,
            Category          = category,
            ManifestYaml      = yaml,
            ManifestSha256    = sha256,
            Status            = ListingStatus.Pending,
            Verified          = false
        };

        _db.PluginListings.Add(listing);
        await _db.SaveChangesAsync();

        return new SubmitPluginResponse(listing.Id, ListingStatusDto.Pending);
    }

    public async Task<RatingDto?> SubmitRatingAsync(string pluginId, Guid userId, SubmitRatingRequest request)
    {
        if (request.Stars < 1 || request.Stars > 5)
            throw new ArgumentException("Stars must be 1-5");

        var listing = await _db.PluginListings.FirstOrDefaultAsync(l => l.PluginId == pluginId);
        if (listing == null || listing.Status != ListingStatus.Approved) return null;

        var existing = await _db.PluginRatings
            .FirstOrDefaultAsync(r => r.ListingId == listing.Id && r.UserId == userId);

        if (existing != null)
        {
            existing.Stars        = request.Stars;
            existing.Comment      = request.Comment;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.PluginRatings.Add(new PluginRating
            {
                ListingId = listing.Id,
                UserId    = userId,
                Stars     = request.Stars,
                Comment   = request.Comment
            });
        }

        await _db.SaveChangesAsync();

        // Recompute aggregate
        var all = await _db.PluginRatings.Where(r => r.ListingId == listing.Id).ToListAsync();
        listing.AverageRating = all.Average(r => r.Stars);
        listing.RatingCount   = all.Count;
        listing.UpdatedAtUtc  = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new RatingDto(request.Stars, request.Comment, DateTime.UtcNow);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static PluginListingDto MapToDto(PluginListing l) => new(
        l.PluginId, l.Name, l.AuthorDisplayName, l.Description, l.Category,
        l.Downloads, l.AverageRating, l.RatingCount, l.Verified);

    private static PluginDetailDto MapToDetail(PluginListing l) => new(
        l.PluginId, l.Name, l.AuthorDisplayName, l.Description, l.Category,
        l.ManifestYaml, l.Signature, l.Verified,
        l.Downloads, l.AverageRating, l.RatingCount);

    /// <summary>Extracts a scalar value for a top-level YAML key like "id: my-plugin".</summary>
    private static string? ExtractYamlField(string yaml, string key)
    {
        foreach (var line in yaml.Split('\n'))
        {
            var trimmed = line.TrimStart();
            var prefix  = key + ":";
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                var value = trimmed[prefix.Length..].Trim().Trim('"', '\'');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        return null;
    }
}
