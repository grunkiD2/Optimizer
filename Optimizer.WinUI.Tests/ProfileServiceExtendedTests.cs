using System;
using System.Collections.Generic;
using System.Text.Json;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Extended ProfileService tests covering edge cases not in ProfileServiceTests.cs.
/// </summary>
[Collection("ProfileServiceCollection")]
public class ProfileServiceExtendedTests
{
    private static Mock<IWindowsOptimizerService> BuildOptimizerMock(
        IEnumerable<string>? optimizations = null)
    {
        var mock = new Mock<IWindowsOptimizerService>();
        mock.Setup(o => o.GetBuiltInPresets()).Returns(new List<SettingsProfile>
        {
            new() { Id = "preset-gaming", Name = "Gaming" }
        });
        mock.Setup(o => o.GetAvailableOptimizationsAsync())
            .ReturnsAsync(optimizations ?? new List<string> { "opt-a", "opt-b" });
        mock.Setup(o => o.IsOptimizationApplied(It.IsAny<string>())).Returns(false);
        mock.Setup(o => o.ApplyProfileAsync(It.IsAny<string>())).ReturnsAsync(true);
        mock.Setup(o => o.ApplyOptimizationAsync(It.IsAny<string>()))
            .ReturnsAsync(new OptimizationResult { Success = true });
        return mock;
    }

    // ── Delete edge cases ─────────────────────────────────────────────────────

    [Fact]
    public void DeleteSnapshot_NonExistentId_IsNoOp()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        // No snapshot has been added — deleting a made-up ID should not throw
        service.DeleteSnapshot("does-not-exist-guid");
        Assert.Empty(service.Snapshots);
    }

    [Fact]
    public void DeleteSnapshot_AfterMultipleSnapshots_DeletesOnlyTarget()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        service.ImportFromJson(JsonSerializer.Serialize(new SettingsProfile { Id = "snap-1", Name = "One" }));
        service.ImportFromJson(JsonSerializer.Serialize(new SettingsProfile { Id = "snap-2", Name = "Two" }));
        Assert.Equal(2, service.Snapshots.Count);

        service.DeleteSnapshot("snap-1");

        Assert.Single(service.Snapshots);
        Assert.Equal("snap-2", service.Snapshots[0].Id);
    }

    // ── Import mixed array+object ─────────────────────────────────────────────

    [Fact]
    public void ImportFromJson_ArrayOfProfiles_AddsAll()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var list = new List<SettingsProfile>
        {
            new() { Id = "m-1", Name = "Mixed 1" },
            new() { Id = "m-2", Name = "Mixed 2" },
            new() { Id = "m-3", Name = "Mixed 3" }
        };
        service.ImportFromJson(JsonSerializer.Serialize(list));

        Assert.Equal(3, service.Snapshots.Count);
    }

    [Fact]
    public void ImportFromJson_SingleObject_AddsExactlyOne()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var profile = new SettingsProfile { Id = "solo", Name = "Solo Profile" };
        service.ImportFromJson(JsonSerializer.Serialize(profile));

        Assert.Single(service.Snapshots);
    }

    // ── Update / Id preservation ──────────────────────────────────────────────

    [Fact]
    public void ImportFromJson_PreservesIdFromJson()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var profile = new SettingsProfile { Id = "stable-guid-123", Name = "Stable" };
        service.ImportFromJson(JsonSerializer.Serialize(profile));

        Assert.Equal("stable-guid-123", service.Snapshots[0].Id);
    }

    [Fact]
    public void ImportFromJson_ValidProfileInArray_IsAdded()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        // Standard array import — valid profile should be added
        var json = "[{\"Id\": \"valid-id\", \"Name\": \"Valid\"}]";
        service.ImportFromJson(json);

        Assert.Single(service.Snapshots);
        Assert.Equal("valid-id", service.Snapshots[0].Id);
    }

    // ── ExportAll roundtrip ───────────────────────────────────────────────────

    [Fact]
    public void ExportAll_AfterImport_RoundTripsCorrectly()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var profile = new SettingsProfile { Id = "rt-id", Name = "RoundTrip" };
        service.ImportFromJson(JsonSerializer.Serialize(profile));

        var exported = service.ExportAll();
        var list = JsonSerializer.Deserialize<List<SettingsProfile>>(exported);

        Assert.NotNull(list);
        Assert.Single(list!);
        Assert.Equal("rt-id", list![0].Id);
    }
}
