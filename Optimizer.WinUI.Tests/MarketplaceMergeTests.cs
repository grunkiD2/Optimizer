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
/// Tests for cloud-merge behavior and Source badge properties of MarketplaceEntry.
/// </summary>
public class MarketplaceMergeTests
{
    // ── Source badge color ────────────────────────────────────────────────────

    [Fact]
    public void SourceBadgeColor_Bundled_IsGray()
    {
        var entry = new MarketplaceEntry { Source = "Bundled" };
        Assert.Equal("#6B7280", entry.SourceBadgeColor);
    }

    [Fact]
    public void SourceBadgeColor_Community_IsGreen()
    {
        var entry = new MarketplaceEntry { Source = "Community" };
        Assert.Equal("#34D399", entry.SourceBadgeColor);
    }

    [Fact]
    public void SourceBadgeColor_Featured_IsPurple()
    {
        var entry = new MarketplaceEntry { Source = "Featured" };
        Assert.Equal("#A78BFA", entry.SourceBadgeColor);
    }

    [Fact]
    public void SourceBadgeColor_Default_IsGray()
    {
        var entry = new MarketplaceEntry();  // default Source is "Bundled"
        Assert.Equal("#6B7280", entry.SourceBadgeColor);
    }

    // ── Merge logic ───────────────────────────────────────────────────────────

    [Fact]
    public void Merge_PrefersLocalForSameId()
    {
        var local = new List<MarketplaceEntry>
        {
            new() { Id = "shared-id", Name = "Local Version", Source = "Bundled" }
        };
        var remote = new List<MarketplaceEntry>
        {
            new() { Id = "shared-id", Name = "Remote Version", Source = "Community" },
            new() { Id = "remote-only", Name = "Remote Only", Source = "Community" }
        };

        var localIds = new HashSet<string>(local.Select(l => l.Id));
        var merged = local.Concat(remote.Where(r => !localIds.Contains(r.Id))).ToList();

        Assert.Equal(2, merged.Count);
        // The local version of shared-id was kept
        var shared = merged.First(e => e.Id == "shared-id");
        Assert.Equal("Local Version", shared.Name);
        Assert.Equal("Bundled", shared.Source);
        // remote-only was appended
        Assert.Contains(merged, e => e.Id == "remote-only");
    }

    [Fact]
    public async Task LoadCatalog_NotAuthenticated_ReturnsLocalOnly()
    {
        var cloud = new Mock<IOptimizerCloudClient>();
        cloud.Setup(c => c.IsAuthenticated).Returns(false);
        var optimizer = new Mock<IWindowsOptimizerService>();

        var svc = new MarketplaceService(optimizer.Object, cloud.Object);
        // catalog file doesn't exist in test bin — returns empty list
        var result = await svc.LoadCatalogAsync(includeRemote: true);

        Assert.NotNull(result);
        // Cloud should not have been called since not authenticated
        cloud.Verify(c => c.BrowseMarketplaceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadCatalog_IncludeRemoteFalse_SkipsCloud()
    {
        var cloud = new Mock<IOptimizerCloudClient>();
        cloud.Setup(c => c.IsAuthenticated).Returns(true);
        var optimizer = new Mock<IWindowsOptimizerService>();

        var svc = new MarketplaceService(optimizer.Object, cloud.Object);
        var result = await svc.LoadCatalogAsync(includeRemote: false);

        Assert.NotNull(result);
        cloud.Verify(c => c.BrowseMarketplaceAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public async Task LoadCatalog_Authenticated_CallsCloud()
    {
        var cloud = new Mock<IOptimizerCloudClient>();
        cloud.Setup(c => c.IsAuthenticated).Returns(true);
        cloud.Setup(c => c.BrowseMarketplaceAsync(null, null, "downloads", 1, 100))
            .ReturnsAsync(new RemoteMarketplaceBrowseResult(0, 1, 100, new List<RemoteMarketplaceListing>()));
        var optimizer = new Mock<IWindowsOptimizerService>();

        var svc = new MarketplaceService(optimizer.Object, cloud.Object);
        await svc.LoadCatalogAsync(includeRemote: true);

        cloud.Verify(c => c.BrowseMarketplaceAsync(null, null, "downloads", 1, 100), Times.Once);
    }

    [Fact]
    public void FeaturedRemoteListing_HasFeaturedSource()
    {
        var featured = new RemoteMarketplaceListing(
            "feat-1", "Featured Pack", "Author", "Desc", "Gaming",
            new List<string>(), new List<string>(), 100, 4.9, 50, true, Featured: true);

        var entry = new MarketplaceEntry
        {
            Source = featured.Featured ? "Featured" : "Community"
        };

        Assert.Equal("Featured", entry.Source);
        Assert.Equal("#A78BFA", entry.SourceBadgeColor);
    }

    [Fact]
    public void CommunityRemoteListing_HasCommunitySource()
    {
        var community = new RemoteMarketplaceListing(
            "comm-1", "Community Pack", "Author", "Desc", "Gaming",
            new List<string>(), new List<string>(), 50, 3.5, 10, false, Featured: false);

        var entry = new MarketplaceEntry
        {
            Source = community.Featured ? "Featured" : "Community"
        };

        Assert.Equal("Community", entry.Source);
        Assert.Equal("#34D399", entry.SourceBadgeColor);
    }
}
