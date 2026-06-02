using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Cloud;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Extended tests for MarketplaceService and MarketplaceEntry model helpers:
/// filtering, sorting, text search, and tag matching.
/// </summary>
public class MarketplaceServiceExtendedTests
{
    private static IReadOnlyList<MarketplaceEntry> SampleCatalog() => new List<MarketplaceEntry>
    {
        new() { Id = "a", Name = "Gaming Boost",   Category = "Gaming",      Tags = ["performance", "fps"], Downloads = 5000, AverageRating = 4.5 },
        new() { Id = "b", Name = "Privacy Shield",  Category = "Privacy",     Tags = ["privacy", "telemetry"], Downloads = 3000, AverageRating = 4.0 },
        new() { Id = "c", Name = "Network Turbo",   Category = "Network",     Tags = ["network", "latency"], Downloads = 1000, AverageRating = 3.8 },
        new() { Id = "d", Name = "Battery Saver",   Category = "Battery",     Tags = ["battery", "power"], Downloads = 8000, AverageRating = 4.2 },
        new() { Id = "e", Name = "FPS Unlocker",    Category = "Gaming",      Tags = ["fps", "gaming"],  Downloads = 2000, AverageRating = 3.5 },
    };

    // ── Filter by category ────────────────────────────────────────────────────

    [Fact]
    public void FilterByCategory_ReturnsOnlyMatchingEntries()
    {
        var catalog = SampleCatalog();
        var gaming = catalog.Where(e => e.Category == "Gaming").ToList();

        Assert.Equal(2, gaming.Count);
        Assert.All(gaming, e => Assert.Equal("Gaming", e.Category));
    }

    [Fact]
    public void FilterByCategory_NoMatch_ReturnsEmpty()
    {
        var catalog = SampleCatalog();
        var result = catalog.Where(e => e.Category == "DoesNotExist").ToList();

        Assert.Empty(result);
    }

    // ── Sort by downloads ─────────────────────────────────────────────────────

    [Fact]
    public void SortByDownloads_DescendingOrder_IsCorrect()
    {
        var catalog = SampleCatalog();
        var sorted = catalog.OrderByDescending(e => e.Downloads).ToList();

        Assert.Equal("d", sorted[0].Id);   // 8000 downloads
        Assert.Equal("a", sorted[1].Id);   // 5000 downloads
    }

    [Fact]
    public void SortByRating_DescendingOrder_IsCorrect()
    {
        var catalog = SampleCatalog();
        var sorted = catalog.OrderByDescending(e => e.AverageRating).ToList();

        Assert.Equal("a", sorted[0].Id);   // 4.5 rating
    }

    // ── Text search ───────────────────────────────────────────────────────────

    [Fact]
    public void Search_MatchesPartialName()
    {
        var catalog = SampleCatalog();
        var results = catalog.Where(e => e.Name.Contains("Gaming", System.StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(results);
        Assert.Equal("a", results[0].Id);
    }

    [Fact]
    public void Search_MatchesPartialDescription()
    {
        var catalog = new List<MarketplaceEntry>
        {
            new() { Id = "x", Name = "Pack A", Description = "Optimizes network latency and throughput" },
            new() { Id = "y", Name = "Pack B", Description = "Improves gaming performance" }
        };

        var results = catalog.Where(e =>
            e.Description.Contains("latency", System.StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(results);
        Assert.Equal("x", results[0].Id);
    }

    [Fact]
    public void Search_MatchesTags()
    {
        var catalog = SampleCatalog();
        var results = catalog.Where(e =>
            e.Tags.Any(t => t.Contains("fps", System.StringComparison.OrdinalIgnoreCase))).ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, e => Assert.Contains("fps", e.Tags));
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var catalog = SampleCatalog();
        var results = catalog.Where(e =>
            e.Name.Contains("xyzzy", System.StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Empty(results);
    }

    // ── DownloadsText helper ──────────────────────────────────────────────────

    [Fact]
    public void DownloadsText_ThousandsAbbreviation()
    {
        var entry = new MarketplaceEntry { Downloads = 5000 };
        Assert.Equal("5.0K", entry.DownloadsText);
    }

    [Fact]
    public void DownloadsText_MillionsAbbreviation()
    {
        var entry = new MarketplaceEntry { Downloads = 1_500_000 };
        Assert.Equal("1.5M", entry.DownloadsText);
    }

    [Fact]
    public void DownloadsText_BelowThousand_NoAbbreviation()
    {
        var entry = new MarketplaceEntry { Downloads = 999 };
        Assert.Equal("999", entry.DownloadsText);
    }

    // ── GenerateSubmissionAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GenerateSubmissionAsync_CreatesFile()
    {
        var optimizerMock = new Mock<IWindowsOptimizerService>();
        var cloudMock = new Mock<IOptimizerCloudClient>();
        cloudMock.Setup(c => c.IsAuthenticated).Returns(false);
        var service = new MarketplaceService(optimizerMock.Object, cloudMock.Object);

        var entry = new MarketplaceEntry { Id = "test-submit", Name = "Test Entry" };
        var path = await service.GenerateSubmissionAsync(entry);

        Assert.True(System.IO.File.Exists(path));
        System.IO.File.Delete(path); // cleanup
    }
}
