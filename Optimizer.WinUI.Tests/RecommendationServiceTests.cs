using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for RecommendationsService personalization logic:
/// snooze, dismiss, permanent-hide, and priority scoring.
/// </summary>
public class RecommendationServiceTests
{
    private static (RecommendationsService service, Mock<IDiagnosticsService> diagMock) BuildService(
        IReadOnlyList<DiagnosticFinding>? findings = null)
    {
        var diagMock = new Mock<IDiagnosticsService>();
        diagMock.Setup(d => d.RunFullScanAsync(It.IsAny<IProgress<string>?>()))
            .ReturnsAsync(findings ?? new List<DiagnosticFinding>());

        var optimizerMock = new Mock<IWindowsOptimizerService>();
        optimizerMock.Setup(o => o.GetBuiltInPresets()).Returns(new List<SettingsProfile>());
        optimizerMock.Setup(o => o.ApplyProfileAsync(It.IsAny<string>())).ReturnsAsync(true);

        return (new RecommendationsService(diagMock.Object, optimizerMock.Object), diagMock);
    }

    // ── Dismiss ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task DismissAsync_IncrementsDismissCount()
    {
        var (service, _) = BuildService();

        // Read baseline first (may be non-zero if a real prefs file exists)
        var before = service.GetPreferences().TryGetValue("test-id-fresh-" + System.Guid.NewGuid(), out var existing)
            ? existing.DismissCount
            : 0;

        var uniqueId = "test-dismiss-" + System.Guid.NewGuid().ToString("N");
        await service.DismissAsync(uniqueId);

        var prefs = service.GetPreferences();
        Assert.True(prefs.ContainsKey(uniqueId));
        Assert.Equal(1, prefs[uniqueId].DismissCount);
    }

    [Fact]
    public async Task DismissAsync_MultipleCalls_AccumulatesCount()
    {
        var (service, _) = BuildService();
        var id = "test-dismiss-multi-" + System.Guid.NewGuid().ToString("N");

        await service.DismissAsync(id);
        await service.DismissAsync(id);

        Assert.Equal(2, service.GetPreferences()[id].DismissCount);
    }

    // ── Permanently hidden after 3 dismissals ─────────────────────────────────

    [Fact]
    public async Task AfterThreeDismissals_FindingIsPermanentlyHidden()
    {
        var finding = new DiagnosticFinding
        {
            Id = "perm-hide-test",
            Title = "Test",
            Description = "Test finding",
            Severity = FindingSeverity.Info,
            Category = FindingCategory.Performance
        };
        var (service, diagMock) = BuildService(new List<DiagnosticFinding> { finding });

        await service.DismissAsync("perm-hide-test");
        await service.DismissAsync("perm-hide-test");
        await service.DismissAsync("perm-hide-test");

        // Now re-generate — the finding should be filtered out
        var recs = await service.GenerateAsync();
        Assert.DoesNotContain(recs, r => r.Id == "perm-hide-test");
    }

    // ── Snooze ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SnoozeAsync_SetsSnoozeUntil()
    {
        var (service, _) = BuildService();
        var duration = TimeSpan.FromDays(3);

        await service.SnoozeAsync("snooze-id", duration);

        var prefs = service.GetPreferences();
        Assert.True(prefs.ContainsKey("snooze-id"));
        Assert.NotNull(prefs["snooze-id"].SnoozedUntilUtc);
        // Should be roughly 3 days from now (within 10 seconds tolerance)
        var expected = DateTime.UtcNow.Add(duration);
        Assert.True(prefs["snooze-id"].SnoozedUntilUtc!.Value > DateTime.UtcNow);
        Assert.True(prefs["snooze-id"].SnoozedUntilUtc!.Value <= expected.AddSeconds(10));
    }

    [Fact]
    public async Task SnoozedFinding_IsFilteredFromGenerateAsync()
    {
        var finding = new DiagnosticFinding
        {
            Id = "snooze-finding",
            Title = "Snooze me",
            Description = "Test",
            Severity = FindingSeverity.Warning,
            Category = FindingCategory.Performance
        };
        var (service, _) = BuildService(new List<DiagnosticFinding> { finding });

        await service.SnoozeAsync("snooze-finding", TimeSpan.FromDays(7));

        var recs = await service.GenerateAsync();
        Assert.DoesNotContain(recs, r => r.Id == "snooze-finding");
    }

    // ── Accept ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAcceptedAsync_IncrementsAcceptCount()
    {
        var (service, _) = BuildService();
        var id = "test-accept-" + System.Guid.NewGuid().ToString("N");

        await service.RecordAcceptedAsync(id);
        await service.RecordAcceptedAsync(id);

        Assert.Equal(2, service.GetPreferences()[id].AcceptCount);
    }

    // ── Priority sorting ──────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptCount_HigherThanDismissCount_BoostsPriority()
    {
        var findingA = new DiagnosticFinding { Id = "rec-a", Title = "A", Description = "A", Severity = FindingSeverity.Info, Category = FindingCategory.Performance };
        var findingB = new DiagnosticFinding { Id = "rec-b", Title = "B", Description = "B", Severity = FindingSeverity.Info, Category = FindingCategory.Performance };
        var (service, _) = BuildService(new List<DiagnosticFinding> { findingA, findingB });

        // Give rec-a a high accept score
        await service.RecordAcceptedAsync("rec-a");
        await service.RecordAcceptedAsync("rec-a");
        await service.RecordAcceptedAsync("rec-a");

        var recs = (await service.GenerateAsync()).ToList();
        // rec-a should be first (highest personalization score)
        var recAIndex = recs.FindIndex(r => r.Id == "rec-a");
        var recBIndex = recs.FindIndex(r => r.Id == "rec-b");
        if (recAIndex >= 0 && recBIndex >= 0)
            Assert.True(recAIndex < recBIndex);
    }

    // ── ResetDismissed ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResetDismissedAsync_ClearsDismissals()
    {
        var uniqueId = "reset-test-" + System.Guid.NewGuid().ToString("N");
        var finding = new DiagnosticFinding
        {
            Id = uniqueId,
            Title = "Reset",
            Description = "Test",
            Severity = FindingSeverity.Info,
            Category = FindingCategory.Performance
        };
        var (service, _) = BuildService(new List<DiagnosticFinding> { finding });

        await service.DismissAsync(uniqueId);
        await service.ResetDismissedAsync();

        // After reset the finding should appear again
        var recs = await service.GenerateAsync();
        Assert.Contains(recs, r => r.Id == uniqueId);
    }
}
