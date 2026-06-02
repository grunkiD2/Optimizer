using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using System.Text.Json;

namespace Optimizer.Server.Services;

public class MarketplaceService : IMarketplaceService
{
    private readonly OptimizerDbContext _db;

    public MarketplaceService(OptimizerDbContext db) => _db = db;

    public async Task<MarketplaceBrowseResponse> BrowseAsync(string? category, string? search, string sortBy, int page, int pageSize)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var query = _db.MarketplaceListings.Where(l => l.Status == ListingStatus.Approved);

        if (!string.IsNullOrEmpty(category) && category != "All")
            query = query.Where(l => l.Category == category);

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLowerInvariant();
            query = query.Where(l =>
                l.Name.ToLower().Contains(s) ||
                l.Description.ToLower().Contains(s) ||
                l.TagsJson.ToLower().Contains(s));
        }

        query = sortBy?.ToLowerInvariant() switch
        {
            "rating" => query.OrderByDescending(l => l.AverageRating).ThenByDescending(l => l.RatingCount),
            "newest" => query.OrderByDescending(l => l.CreatedAtUtc),
            "az" => query.OrderBy(l => l.Name),
            _ => query.OrderByDescending(l => l.Featured).ThenByDescending(l => l.Downloads)  // featured first, then most downloaded
        };

        var total = await query.CountAsync();
        var listings = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new MarketplaceBrowseResponse(total, page, pageSize, listings.Select(Map).ToList());
    }

    public async Task<MarketplaceListingDto?> GetByPublicIdAsync(string publicId)
    {
        var listing = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.PublicId == publicId && l.Status == ListingStatus.Approved);
        return listing == null ? null : Map(listing);
    }

    public async Task<bool> IncrementDownloadAsync(string publicId)
    {
        var listing = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.PublicId == publicId && l.Status == ListingStatus.Approved);
        if (listing == null) return false;
        listing.Downloads++;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<SubmitListingResponse> SubmitAsync(Guid userId, SubmitListingRequest request)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 80)
            throw new ArgumentException("Name required, max 80 chars");
        if (request.Description.Length > 500)
            throw new ArgumentException("Description max 500 chars");
        if (request.Optimizations.Count == 0)
            throw new ArgumentException("At least one optimization required");

        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");

        // Generate a stable PublicId from name + user
        var slug = string.Join("-", request.Name.ToLower().Split(new[] { ' ', '_' }, StringSplitOptions.RemoveEmptyEntries))
                         .Replace(",", "", StringComparison.Ordinal)
                         .Replace(".", "", StringComparison.Ordinal)
                         .Replace("'", "", StringComparison.Ordinal);
        var publicId = $"mkt-{slug}-{Guid.NewGuid().ToString()[..8]}";

        var listing = new MarketplaceListing
        {
            AuthorId = userId,
            PublicId = publicId,
            Name = request.Name,
            AuthorDisplayName = user.DisplayName,
            Description = request.Description,
            Category = request.Category,
            TagsJson = JsonSerializer.Serialize(request.Tags),
            OptimizationsJson = JsonSerializer.Serialize(request.Optimizations),
            Status = ListingStatus.Pending,  // requires moderation
            Verified = false,
            Featured = false
        };

        _db.MarketplaceListings.Add(listing);
        await _db.SaveChangesAsync();

        return new SubmitListingResponse(listing.Id, ListingStatusDto.Pending);
    }

    public async Task<RatingDto?> SubmitRatingAsync(string publicId, Guid userId, SubmitRatingRequest request)
    {
        if (request.Stars < 1 || request.Stars > 5) throw new ArgumentException("Stars must be 1-5");

        var listing = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.PublicId == publicId);
        if (listing == null || listing.Status != ListingStatus.Approved) return null;

        var existing = await _db.MarketplaceRatings.FirstOrDefaultAsync(r => r.ListingId == listing.Id && r.UserId == userId);
        if (existing != null)
        {
            existing.Stars = request.Stars;
            existing.Comment = request.Comment;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _db.MarketplaceRatings.Add(new MarketplaceRating
            {
                ListingId = listing.Id,
                UserId = userId,
                Stars = request.Stars,
                Comment = request.Comment
            });
        }

        await _db.SaveChangesAsync();

        // Recompute aggregate
        var all = await _db.MarketplaceRatings.Where(r => r.ListingId == listing.Id).ToListAsync();
        listing.AverageRating = all.Average(r => r.Stars);
        listing.RatingCount = all.Count;
        listing.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new RatingDto(request.Stars, request.Comment, DateTime.UtcNow);
    }

    public async Task<bool> ReportAsync(string publicId, Guid reporterId, ReportListingRequest request)
    {
        var listing = await _db.MarketplaceListings.FirstOrDefaultAsync(l => l.PublicId == publicId);
        if (listing == null) return false;

        if (!Enum.TryParse<ReportReason>(request.Reason, true, out var reason))
            reason = ReportReason.Other;

        _db.MarketplaceReports.Add(new MarketplaceReport
        {
            ListingId = listing.Id,
            ReporterUserId = reporterId,
            Reason = reason,
            Comment = request.Comment
        });
        await _db.SaveChangesAsync();
        return true;
    }

    private static MarketplaceListingDto Map(MarketplaceListing l) => new(
        l.Id,
        l.PublicId,
        l.Name,
        l.AuthorDisplayName,
        l.Description,
        l.Category,
        JsonSerializer.Deserialize<List<string>>(l.TagsJson) ?? new(),
        JsonSerializer.Deserialize<List<string>>(l.OptimizationsJson) ?? new(),
        l.Downloads,
        l.AverageRating,
        l.RatingCount,
        l.Verified,
        l.Featured);
}
