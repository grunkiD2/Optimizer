using System;
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
/// Tests for FederatedClient orchestration.
/// Verifies privacy guarantees: no upload when disabled, only DP-noised data when enabled.
/// </summary>
public class FederatedClientTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AppSettings MakeSettings(bool federatedEnabled, bool cloudSyncEnabled = true)
        => new() { FederatedLearningEnabled = federatedEnabled, CloudSyncEnabled = cloudSyncEnabled };

    private static Mock<ISettingsService> MockSettings(AppSettings settings)
    {
        var mock = new Mock<ISettingsService>();
        mock.Setup(s => s.Settings).Returns(settings);
        return mock;
    }

    private static Mock<IOptimizerCloudClient> MockCloud(bool authenticated)
    {
        var mock = new Mock<IOptimizerCloudClient>();
        mock.Setup(c => c.IsAuthenticated).Returns(authenticated);
        mock.Setup(c => c.ContributeFederatedAsync(It.IsAny<IReadOnlyList<FederatedCategoryContribution>>()))
            .ReturnsAsync(true);
        mock.Setup(c => c.GetCommunityBaselinesAsync())
            .ReturnsAsync(new List<FederatedCommunityBaseline>
            {
                new("Performance", 0.72, 12)
            });
        return mock;
    }

    private static Mock<IIntelligenceService> MockIntelligence(
        Dictionary<string, double>? rates = null)
    {
        var mock = new Mock<IIntelligenceService>();

        var summary = new LocalModelSummary(
            CategoryAcceptanceRates: (rates ?? new Dictionary<string, double> { ["Performance"] = 0.75 })
                as IReadOnlyDictionary<string, double>,
            TotalSamples: 10,
            ComputedUtc: DateTime.UtcNow);

        mock.Setup(i => i.ComputeLocalSummary()).Returns(summary);
        mock.Setup(i => i.ComputePrivatizedSummary(It.IsAny<double>())).Returns(
            new LocalModelSummary(
                CategoryAcceptanceRates: new Dictionary<string, double> { ["Performance"] = 0.68 },
                TotalSamples: 9,  // slightly noised
                ComputedUtc: DateTime.UtcNow));

        return mock;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SyncAsync_WhenDisabled_DoesNotUpload()
    {
        var settings     = MockSettings(MakeSettings(federatedEnabled: false));
        var cloud        = MockCloud(authenticated: true);
        var intelligence = MockIntelligence();

        var client = new FederatedClient(settings.Object, cloud.Object, intelligence.Object);
        await client.SyncAsync();

        // Contribute must NOT have been called
        cloud.Verify(c => c.ContributeFederatedAsync(It.IsAny<IReadOnlyList<FederatedCategoryContribution>>()),
            Times.Never);
        cloud.Verify(c => c.GetCommunityBaselinesAsync(), Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenNotAuthenticated_DoesNotUpload()
    {
        var settings     = MockSettings(MakeSettings(federatedEnabled: true));
        var cloud        = MockCloud(authenticated: false);
        var intelligence = MockIntelligence();

        var client = new FederatedClient(settings.Object, cloud.Object, intelligence.Object);
        await client.SyncAsync();

        cloud.Verify(c => c.ContributeFederatedAsync(It.IsAny<IReadOnlyList<FederatedCategoryContribution>>()),
            Times.Never);
    }

    [Fact]
    public async Task SyncAsync_WhenEnabled_UploadsPrivatizedNotRawSummary()
    {
        var settings     = MockSettings(MakeSettings(federatedEnabled: true));
        var cloud        = MockCloud(authenticated: true);
        var intelligence = MockIntelligence();

        var client = new FederatedClient(settings.Object, cloud.Object, intelligence.Object);
        await client.SyncAsync();

        // ComputePrivatizedSummary must have been called (not ComputeLocalSummary for upload)
        intelligence.Verify(i => i.ComputePrivatizedSummary(It.IsAny<double>()), Times.Once);
        intelligence.Verify(i => i.ComputeLocalSummary(), Times.Never);

        // Contribute must have been called with the DP-noised rate (0.68), not raw (0.75)
        cloud.Verify(c => c.ContributeFederatedAsync(It.Is<IReadOnlyList<FederatedCategoryContribution>>(
            list => list.Any(x => x.Category == "Performance" && Math.Abs(x.AcceptanceRate - 0.68) < 0.001))),
            Times.Once);
    }

    [Fact]
    public async Task SyncAsync_WhenEnabled_FetchesCommunityBaselines()
    {
        var settings     = MockSettings(MakeSettings(federatedEnabled: true));
        var cloud        = MockCloud(authenticated: true);
        var intelligence = MockIntelligence();

        var client = new FederatedClient(settings.Object, cloud.Object, intelligence.Object);
        await client.SyncAsync();

        cloud.Verify(c => c.GetCommunityBaselinesAsync(), Times.Once);

        // Baselines should be cached on the client
        Assert.Single(client.CommunityBaselines);
        Assert.Equal("Performance", client.CommunityBaselines[0].Category);
        Assert.Equal(0.72, client.CommunityBaselines[0].CommunityAcceptanceRate);
    }
}
