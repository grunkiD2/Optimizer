using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class PluginMarketplaceServiceTests : IDisposable
{
    private readonly OptimizerDbContext _db;
    private readonly PluginMarketplaceService _svc;
    private readonly Guid _userId;

    private static readonly string ValidYaml =
        "manifest_version: 1\nid: test-plugin\nname: Test Plugin\ndescription: A test.\ncategory: Privacy\nchanges:\n  - type: registry\n    path: HKCU\\Test\n    value: X\n    value_type: dword\n    apply: \"1\"\n    revert: \"0\"";

    public PluginMarketplaceServiceTests()
    {
        var opt = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OptimizerDbContext(opt);

        _userId = Guid.NewGuid();
        _db.Users.Add(new User { Id = _userId, Email = "tester@example.com", DisplayName = "Tester" });
        _db.SaveChanges();

        _svc = new PluginMarketplaceService(_db);
    }

    public void Dispose() { _db.Dispose(); GC.SuppressFinalize(this); }

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private void SeedApproved(string pluginId = "test-plugin", string category = "Privacy",
        bool verified = true, string? signature = null)
    {
        _db.PluginListings.Add(new PluginListing
        {
            PluginId          = pluginId,
            Name              = $"Test {pluginId}",
            AuthorDisplayName = "Author",
            Description       = "A test plugin",
            Category          = category,
            ManifestYaml      = ValidYaml,
            ManifestSha256    = "abc123",
            Signature         = signature,
            Downloads         = 50,
            AverageRating     = 4.0,
            RatingCount       = 10,
            Verified          = verified,
            Status            = ListingStatus.Approved
        });
        _db.SaveChanges();
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Browse_ReturnsOnlyApproved()
    {
        _db.PluginListings.Add(new PluginListing
        {
            PluginId = "pending-plugin", Name = "Pending", Category = "Privacy",
            ManifestYaml = ValidYaml, ManifestSha256 = "x",
            Status = ListingStatus.Pending
        });
        SeedApproved("approved-plugin");

        var result = await _svc.BrowseAsync(null, null, "downloads", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Equal("approved-plugin", result.Listings[0].PluginId);
    }

    [Fact]
    public async Task Browse_FiltersByCategory()
    {
        SeedApproved("privacy-plugin", "Privacy");
        SeedApproved("gaming-plugin", "Gaming");

        var result = await _svc.BrowseAsync("Privacy", null, "downloads", 1, 20);

        Assert.Equal(1, result.Total);
        Assert.Equal("privacy-plugin", result.Listings[0].PluginId);
    }

    [Fact]
    public async Task Browse_SearchMatchesName()
    {
        SeedApproved("cortana-plugin", "Privacy");

        var result = await _svc.BrowseAsync(null, "cortana", "downloads", 1, 20);

        Assert.Single(result.Listings);
    }

    // ── GetByPluginId ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPluginId_Returns_ManifestAndSignature()
    {
        SeedApproved("signed-plugin", signature: "FAKESIG==");

        var detail = await _svc.GetByPluginIdAsync("signed-plugin");

        Assert.NotNull(detail);
        Assert.Equal("signed-plugin", detail!.PluginId);
        Assert.Equal(ValidYaml, detail.ManifestYaml);
        Assert.Equal("FAKESIG==", detail.Signature);
    }

    [Fact]
    public async Task GetByPluginId_Nonexistent_ReturnsNull()
    {
        var detail = await _svc.GetByPluginIdAsync("no-such-plugin");
        Assert.Null(detail);
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_CreatesPendingListing()
    {
        var req    = new SubmitPluginRequest(ValidYaml);
        var result = await _svc.SubmitAsync(_userId, req);

        Assert.Equal(ListingStatusDto.Pending, result.Status);
        var stored = await _db.PluginListings.FindAsync(result.Id);
        Assert.NotNull(stored);
        Assert.Equal(ListingStatus.Pending, stored!.Status);
        Assert.False(stored.Verified);
        Assert.Null(stored.Signature);
    }

    [Fact]
    public async Task Submit_ThrowsOnEmptyManifest()
    {
        var req = new SubmitPluginRequest("");
        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    [Fact]
    public async Task Submit_ThrowsOnManifestTooBig()
    {
        var huge = new string('x', 33 * 1024);  // > 32KB
        var yaml = $"manifest_version: 1\nid: big-plugin\nname: Big\nchanges:\n  - type: registry\n    path: HKCU\\x\n    value: {huge}";
        var req  = new SubmitPluginRequest(yaml);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    [Fact]
    public async Task Submit_ThrowsOnMissingRequiredField()
    {
        // Missing 'changes:' field
        var yaml = "manifest_version: 1\nid: no-changes\nname: No Changes";
        var req  = new SubmitPluginRequest(yaml);

        await Assert.ThrowsAsync<ArgumentException>(() => _svc.SubmitAsync(_userId, req));
    }

    // ── Download ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementDownload_BumpsCounter()
    {
        SeedApproved("dl-plugin");
        var before = (await _db.PluginListings.FirstAsync(l => l.PluginId == "dl-plugin")).Downloads;

        var ok = await _svc.IncrementDownloadAsync("dl-plugin");

        Assert.True(ok);
        var after = (await _db.PluginListings.FirstAsync(l => l.PluginId == "dl-plugin")).Downloads;
        Assert.Equal(before + 1, after);
    }

    [Fact]
    public async Task IncrementDownload_Unknown_ReturnsFalse()
    {
        var ok = await _svc.IncrementDownloadAsync("nonexistent");
        Assert.False(ok);
    }

    // ── Rating ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rate_CreatesNewRating()
    {
        SeedApproved("rate-plugin");

        var result = await _svc.SubmitRatingAsync("rate-plugin", _userId, new SubmitRatingRequest(4, "Nice!"));

        Assert.NotNull(result);
        Assert.Equal(4, result!.Stars);
    }

    [Fact]
    public async Task Rate_UpdatesExistingRating()
    {
        SeedApproved("rate-update-plugin");

        await _svc.SubmitRatingAsync("rate-update-plugin", _userId, new SubmitRatingRequest(3, "OK"));
        var updated = await _svc.SubmitRatingAsync("rate-update-plugin", _userId, new SubmitRatingRequest(5, "Great!"));

        Assert.Equal(5, updated!.Stars);
        var count = await _db.PluginRatings.CountAsync(r => r.UserId == _userId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Rate_RecomputesAggregate()
    {
        SeedApproved("avg-plugin");
        var listing = await _db.PluginListings.FirstAsync(l => l.PluginId == "avg-plugin");

        var userId2 = Guid.NewGuid();
        _db.Users.Add(new User { Id = userId2, Email = "u2@example.com", DisplayName = "U2" });
        _db.SaveChanges();

        await _svc.SubmitRatingAsync("avg-plugin", _userId, new SubmitRatingRequest(4, null));
        await _svc.SubmitRatingAsync("avg-plugin", userId2, new SubmitRatingRequest(2, null));

        var refreshed = await _db.PluginListings.FindAsync(listing.Id);
        Assert.Equal(3.0, refreshed!.AverageRating);
        Assert.Equal(2, refreshed.RatingCount);
    }

    [Fact]
    public async Task Rate_InvalidStars_Throws()
    {
        SeedApproved("bad-stars-plugin");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.SubmitRatingAsync("bad-stars-plugin", _userId, new SubmitRatingRequest(0, null)));
    }

    [Fact]
    public async Task Rate_Unknown_ReturnsNull()
    {
        var result = await _svc.SubmitRatingAsync("nonexistent", _userId, new SubmitRatingRequest(3, null));
        Assert.Null(result);
    }
}
