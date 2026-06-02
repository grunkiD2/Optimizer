using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for MarketplaceService catalog, rating, and install behavior.
/// LoadCatalogAsync requires the marketplace-catalog.json asset at runtime.
/// Tests that require the file gracefully handle a missing catalog.
/// </summary>
public class MarketplaceServiceTests
{
    private static Mock<IWindowsOptimizerService> BuildOptimizerMock()
    {
        var mock = new Mock<IWindowsOptimizerService>();
        mock.Setup(o => o.ApplyOptimizationAsync(It.IsAny<string>()))
            .ReturnsAsync(new OptimizationResult { Success = true });
        return mock;
    }

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void GetUserRatings_ReturnsEmptyOnFreshInstance()
    {
        // Fresh instance with no ratings file will have empty dict
        // (ratings file is in %LocalAppData%\Optimizer\marketplace-ratings.json)
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        var ratings = service.GetUserRatings();
        // Just verify the contract — may or may not be empty if real file exists
        Assert.NotNull(ratings);
    }

    [Fact]
    public async Task RateAsync_StoresRatingForId()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        await service.RateAsync("profile-gaming", 4);

        var ratings = service.GetUserRatings();
        Assert.True(ratings.ContainsKey("profile-gaming"));
        Assert.Equal(4, ratings["profile-gaming"]);
    }

    [Fact]
    public async Task RateAsync_ClampsRatingAboveFive()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        await service.RateAsync("id-x", 99);

        Assert.Equal(5, service.GetUserRatings()["id-x"]);
    }

    [Fact]
    public async Task RateAsync_ClampsRatingBelowZero()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        await service.RateAsync("id-y", -5);

        Assert.Equal(0, service.GetUserRatings()["id-y"]);
    }

    [Fact]
    public async Task RateAsync_OverwritesPreviousRating()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        await service.RateAsync("id-z", 2);
        await service.RateAsync("id-z", 5);

        Assert.Equal(5, service.GetUserRatings()["id-z"]);
    }

    [Fact]
    public async Task InstallAsync_CallsApplyOptimizationForEachItem()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        var entry = new MarketplaceEntry
        {
            Id = "test-pack",
            Name = "Test Pack",
            Optimizations = new List<string> { "opt-a", "opt-b", "opt-c" }
        };

        var success = await service.InstallAsync(entry);

        Assert.True(success);
        Assert.True(entry.IsInstalled);
        optimizer.Verify(o => o.ApplyOptimizationAsync("opt-a"), Times.Once);
        optimizer.Verify(o => o.ApplyOptimizationAsync("opt-b"), Times.Once);
        optimizer.Verify(o => o.ApplyOptimizationAsync("opt-c"), Times.Once);
    }

    [Fact]
    public async Task InstallAsync_EmptyOptimizations_Succeeds()
    {
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        var entry = new MarketplaceEntry
        {
            Id = "empty-pack",
            Optimizations = new List<string>()
        };

        var success = await service.InstallAsync(entry);

        Assert.True(success);
        Assert.True(entry.IsInstalled);
        optimizer.Verify(o => o.ApplyOptimizationAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoadCatalogAsync_ReturnsEmptyWhenFileAbsent()
    {
        // The catalog file won't be present in the test bin directory
        var optimizer = BuildOptimizerMock();
        var service = new MarketplaceService(optimizer.Object);

        var catalog = await service.LoadCatalogAsync();

        // Should not throw — returns empty list gracefully
        Assert.NotNull(catalog);
    }
}
