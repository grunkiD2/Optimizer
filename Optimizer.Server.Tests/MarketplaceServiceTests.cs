using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class MarketplaceServiceTests : IDisposable
{
    private readonly OptimizerDbContext _db;
    private readonly MarketplaceService _svc;
    private readonly Guid _userId;

    public MarketplaceServiceTests()
    {
        var opt = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OptimizerDbContext(opt);

        _userId = Guid.NewGuid();
        _db.Users.Add(new User { Id = _userId, Email = "tester@example.com", DisplayName = "Tester" });
        _db.SaveChanges();

        _svc = new MarketplaceService(_db);
    }

    public void Dispose() { _db.Dispose(); GC.SuppressFinalize(this); }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private void SeedApproved(string publicId = "mkt-test", string category = "Gaming", bool featured = false)
    {
        _db.MarketplaceListings.Add(new MarketplaceListing
        {
            PublicId = publicId,
            Name = $"Test {publicId}",
            AuthorDisplayName = "Author",
            Description = "A test listing",
            Category = category,
            TagsJson = """["fps","latency"]""",
            OptimizationsJson = """["DisableBackgroundApps"]""",
            Downloads = 100,
            AverageRating = 3.0,
            RatingCount = 5,
            Verified = true,
            Featured = featured,
            Status = ListingStatus.Approved
        });
        _db.SaveChanges();
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Browse_ReturnsOnlyApprovedListings()
    {
        _db.MarketplaceListings.Add(new MarketplaceListing
        {
            PublicId = "pending-1", Name = "Pending", Category = "Gaming",
            TagsJson = "[]", OptimizationsJson = "[]",
            Status = ListingStatus.Pending
        });
        SeedApproved("approved-1");
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, null, "downloads", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.All(result.Listings, l => Assert.Equal("approved-1", l.PublicId));
    }

    [Fact]
    public async Task Browse_FiltersByCategory()
    {
        SeedApproved("gaming-1", "Gaming");
        SeedApproved("privacy-1", "Privacy");

        var result = await _svc.BrowseAsync("Gaming", null, "downloads", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Equal("gaming-1", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_SearchMatchesName()
    {
        SeedApproved("gaming-1", "Gaming");
        _db.MarketplaceListings.Add(new MarketplaceListing
        {
            PublicId = "special-2", Name = "Special Turbo Pack",
            AuthorDisplayName = "A", Description = "desc",
            Category = "Productivity", TagsJson = "[]", OptimizationsJson = "[]",
            Status = ListingStatus.Approved
        });
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, "Turbo", "downloads", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Equal("special-2", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_SearchMatchesDescription()
    {
        _db.MarketplaceListings.Add(new MarketplaceListing
        {
            PublicId = "desc-search", Name = "Pack Alpha",
            AuthorDisplayName = "A", Description = "Ultimate esports configuration",
            Category = "Gaming", TagsJson = "[]", OptimizationsJson = "[]",
            Status = ListingStatus.Approved
        });
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, "esports", "downloads", 1, 20);

        Assert.Single(result.Listings);
    }

    [Fact]
    public async Task Browse_SearchMatchesTags()
    {
        SeedApproved("tag-search");  // has "fps" tag in TagsJson

        var result = await _svc.BrowseAsync(null, "fps", "downloads", 1, 20);

        Assert.Single(result.Listings);
    }

    [Fact]
    public async Task Browse_SortByRating()
    {
        SeedApproved("low-rating", "Gaming");
        _db.MarketplaceListings.Add(new MarketplaceListing
        {
            PublicId = "high-rating", Name = "High Rated", Category = "Gaming",
            TagsJson = "[]", OptimizationsJson = "[]",
            AverageRating = 5.0, RatingCount = 100,
            Status = ListingStatus.Approved
        });
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, null, "rating", 1, 20);

        Assert.Equal("high-rating", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_SortByNewest()
    {
        SeedApproved("old-entry");
        // Force a newer CreatedAtUtc
        var newEntry = new MarketplaceListing
        {
            PublicId = "new-entry", Name = "New", Category = "Gaming",
            TagsJson = "[]", OptimizationsJson = "[]",
            Status = ListingStatus.Approved,
            CreatedAtUtc = DateTime.UtcNow.AddHours(1)
        };
        _db.MarketplaceListings.Add(newEntry);
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, null, "newest", 1, 20);

        Assert.Equal("new-entry", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_SortByAZ()
    {
        _db.MarketplaceListings.AddRange(
            new MarketplaceListing { PublicId = "z-entry", Name = "Zebra Pack", Category = "Gaming", TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved },
            new MarketplaceListing { PublicId = "a-entry", Name = "Apple Pack", Category = "Gaming", TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved }
        );
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, null, "az", 1, 20);

        Assert.Equal("a-entry", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_SortByDownloads_FeaturedFirst()
    {
        _db.MarketplaceListings.AddRange(
            new MarketplaceListing { PublicId = "normal", Name = "Normal", Downloads = 9999, Featured = false, Category = "G", TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved },
            new MarketplaceListing { PublicId = "featured", Name = "Featured", Downloads = 1, Featured = true, Category = "G", TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved }
        );
        _db.SaveChanges();

        var result = await _svc.BrowseAsync(null, null, "downloads", 1, 20);

        Assert.Equal("featured", result.Listings[0].PublicId);
    }

    [Fact]
    public async Task Browse_Pagination_Works()
    {
        for (var i = 0; i < 5; i++)
        {
            _db.MarketplaceListings.Add(new MarketplaceListing
            {
                PublicId = $"listing-{i}", Name = $"Listing {i}", Category = "Gaming",
                TagsJson = "[]", OptimizationsJson = "[]", Status = ListingStatus.Approved
            });
        }
        _db.SaveChanges();

        var page1 = await _svc.BrowseAsync(null, null, "az", 1, 3);
        var page2 = await _svc.BrowseAsync(null, null, "az", 2, 3);

        Assert.Equal(5, page1.Total);
        Assert.Equal(3, page1.Listings.Count);
        Assert.Equal(2, page2.Listings.Count);
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_CreatesPendingListing()
    {
        var req = new SubmitListingRequest("My Profile", "A great profile", "Gaming", ["fps"], ["DisableBackgroundApps"]);
        var result = await _svc.SubmitAsync(_userId, req);

        Assert.Equal(ListingStatusDto.Pending, result.Status);
        var stored = await _db.MarketplaceListings.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal(ListingStatus.Pending, stored!.Status);
    }

    [Fact]
    public async Task Submit_ThrowsOnEmptyName()
    {
        var req = new SubmitListingRequest("", "desc", "Gaming", [], ["DisableBackgroundApps"]);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    [Fact]
    public async Task Submit_ThrowsOnNameTooLong()
    {
        var req = new SubmitListingRequest(new string('X', 81), "desc", "Gaming", [], ["DisableBackgroundApps"]);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    [Fact]
    public async Task Submit_ThrowsOnDescriptionTooLong()
    {
        var req = new SubmitListingRequest("Name", new string('X', 501), "Gaming", [], ["DisableBackgroundApps"]);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    [Fact]
    public async Task Submit_ThrowsWhenNoOptimizations()
    {
        var req = new SubmitListingRequest("Name", "desc", "Gaming", [], []);
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    // ── Rating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rate_CreatesNewRating()
    {
        SeedApproved("rate-test");

        var result = await _svc.SubmitRatingAsync("rate-test", _userId, new SubmitRatingRequest(4, "Great!"));

        Assert.NotNull(result);
        Assert.Equal(4, result!.Stars);
        Assert.Equal("Great!", result.Comment);
    }

    [Fact]
    public async Task Rate_UpdatesExistingRating()
    {
        SeedApproved("rate-update");

        await _svc.SubmitRatingAsync("rate-update", _userId, new SubmitRatingRequest(3, "OK"));
        var updated = await _svc.SubmitRatingAsync("rate-update", _userId, new SubmitRatingRequest(5, "Amazing!"));

        Assert.Equal(5, updated!.Stars);
        var count = await _db.MarketplaceRatings.CountAsync(r => r.UserId == _userId);
        Assert.Equal(1, count);  // one rating per user per listing
    }

    [Fact]
    public async Task Rate_RecomputesAverageRating()
    {
        SeedApproved("avg-test");
        var listing = await _db.MarketplaceListings.FirstAsync(l => l.PublicId == "avg-test");

        var userId2 = Guid.NewGuid();
        _db.Users.Add(new User { Id = userId2, Email = "u2@example.com", DisplayName = "U2" });
        _db.SaveChanges();

        await _svc.SubmitRatingAsync("avg-test", _userId, new SubmitRatingRequest(4, null));
        await _svc.SubmitRatingAsync("avg-test", userId2, new SubmitRatingRequest(2, null));

        var refreshed = await _db.MarketplaceListings.FindAsync(listing.Id);
        Assert.Equal(3.0, refreshed!.AverageRating);  // (4+2)/2 = 3.0
        Assert.Equal(2, refreshed.RatingCount);
    }

    [Fact]
    public async Task Rate_InvalidStars_Throws()
    {
        SeedApproved("bad-stars");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SubmitRatingAsync("bad-stars", _userId, new SubmitRatingRequest(6, null)));
    }

    [Fact]
    public async Task Rate_UnknownPublicId_ReturnsNull()
    {
        var result = await _svc.SubmitRatingAsync("nonexistent", _userId, new SubmitRatingRequest(3, null));
        Assert.Null(result);
    }

    // ── Download ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementDownload_BumpsCounter()
    {
        SeedApproved("dl-test");
        var before = (await _db.MarketplaceListings.FirstAsync(l => l.PublicId == "dl-test")).Downloads;

        var ok = await _svc.IncrementDownloadAsync("dl-test");

        Assert.True(ok);
        var after = (await _db.MarketplaceListings.FirstAsync(l => l.PublicId == "dl-test")).Downloads;
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task IncrementDownload_UnknownId_ReturnsFalse()
    {
        var ok = await _svc.IncrementDownloadAsync("nonexistent");
        Assert.False(ok);
    }

    // ── Report ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Report_CreatesRecord()
    {
        SeedApproved("report-test");

        var ok = await _svc.ReportAsync("report-test", _userId, new ReportListingRequest("Spam", "This is spam"));

        Assert.True(ok);
        var report = await _db.MarketplaceReports.FirstOrDefaultAsync(r => r.ReporterUserId == _userId);
        Assert.NotNull(report);
        Assert.Equal(ReportReason.Spam, report!.Reason);
    }

    [Fact]
    public async Task Report_UnknownPublicId_ReturnsFalse()
    {
        var ok = await _svc.ReportAsync("nonexistent", _userId, new ReportListingRequest("Other", null));
        Assert.False(ok);
    }
}
