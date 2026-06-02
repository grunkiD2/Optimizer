using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for ProfileService snapshot management.
/// NOTE: ProfileService writes to %LocalAppData%\Optimizer\snapshots.json.
/// Tests are designed to be side-effect safe where possible.
/// </summary>
public class ProfileServiceTests
{
    private static Mock<IWindowsOptimizerService> BuildOptimizerMock(
        IReadOnlyList<SettingsProfile>? presets = null,
        IEnumerable<string>? optimizations = null)
    {
        var mock = new Mock<IWindowsOptimizerService>();
        mock.Setup(o => o.GetBuiltInPresets())
            .Returns(presets ?? new List<SettingsProfile>
            {
                new() { Id = "gaming", Name = "Gaming" },
                new() { Id = "productivity", Name = "Productivity" }
            });
        mock.Setup(o => o.GetAvailableOptimizationsAsync())
            .ReturnsAsync(optimizations ?? new List<string> { "opt-a", "opt-b" });
        mock.Setup(o => o.IsOptimizationApplied(It.IsAny<string>()))
            .Returns(false);
        mock.Setup(o => o.ApplyProfileAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        mock.Setup(o => o.ApplyOptimizationAsync(It.IsAny<string>()))
            .ReturnsAsync(new OptimizationResult { Success = true });
        return mock;
    }

    [Fact]
    public void BuiltInPresets_DelegatesToOptimizer()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        Assert.Equal(2, service.BuiltInPresets.Count);
        Assert.Contains(service.BuiltInPresets, p => p.Id == "gaming");
    }

    [Fact]
    public void Snapshots_StartsEmpty()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        // A freshly constructed service has no snapshots until Load is called
        Assert.Empty(service.Snapshots);
    }

    [Fact]
    public async Task SaveSnapshotAsync_AddsToSnapshots()
    {
        var mock = BuildOptimizerMock(optimizations: new[] { "opt-a" });
        mock.Setup(o => o.IsOptimizationApplied("opt-a")).Returns(true);
        var service = new ProfileService(mock.Object);

        await service.SaveSnapshotAsync("My Snapshot");

        Assert.Single(service.Snapshots);
        Assert.Equal("My Snapshot", service.Snapshots[0].Name);
    }

    [Fact]
    public async Task SaveSnapshotAsync_CapturesAppliedOptimizations()
    {
        var mock = BuildOptimizerMock(optimizations: new[] { "opt-a", "opt-b" });
        mock.Setup(o => o.IsOptimizationApplied("opt-a")).Returns(true);
        mock.Setup(o => o.IsOptimizationApplied("opt-b")).Returns(false);
        var service = new ProfileService(mock.Object);

        await service.SaveSnapshotAsync("Test");

        var snap = service.Snapshots[0];
        Assert.Contains("opt-a", snap.Optimizations);
        Assert.DoesNotContain("opt-b", snap.Optimizations);
    }

    [Fact]
    public async Task DeleteSnapshot_RemovesById()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        await service.SaveSnapshotAsync("TempSnap");
        Assert.Single(service.Snapshots);

        var id = service.Snapshots[0].Id;
        service.DeleteSnapshot(id);

        Assert.Empty(service.Snapshots);
    }

    [Fact]
    public void ExportAll_ReturnsValidJson()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var json = service.ExportAll();

        // Should deserialize cleanly as an array
        var list = JsonSerializer.Deserialize<List<SettingsProfile>>(json);
        Assert.NotNull(list);
    }

    [Fact]
    public void ImportFromJson_EmptyString_ThrowsArgumentException()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        Assert.Throws<ArgumentException>(() => service.ImportFromJson(""));
    }

    [Fact]
    public void ImportFromJson_InvalidJson_ThrowsInvalidDataException()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        Assert.Throws<InvalidDataException>(() => service.ImportFromJson("not json {{{"));
    }

    [Fact]
    public void ImportFromJson_ValidSingleObject_AddsSnapshot()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var json = JsonSerializer.Serialize(new SettingsProfile
        {
            Id = "imported-1",
            Name = "Imported Profile",
            ProfileType = ProfileType.Custom
        });

        service.ImportFromJson(json);

        Assert.Single(service.Snapshots);
        Assert.Equal("Imported Profile", service.Snapshots[0].Name);
    }

    [Fact]
    public void ImportFromJson_ValidArray_AddsAll()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var profiles = new List<SettingsProfile>
        {
            new() { Id = "imp-a", Name = "A" },
            new() { Id = "imp-b", Name = "B" }
        };
        var json = JsonSerializer.Serialize(profiles);

        service.ImportFromJson(json);

        Assert.Equal(2, service.Snapshots.Count);
    }

    [Fact]
    public void ImportFromJson_DuplicateId_SkipsDuplicate()
    {
        var mock = BuildOptimizerMock();
        var service = new ProfileService(mock.Object);

        var profile = new SettingsProfile { Id = "dup-id", Name = "First" };
        service.ImportFromJson(JsonSerializer.Serialize(profile));
        Assert.Single(service.Snapshots);

        // Import same ID again — should not add
        var profile2 = new SettingsProfile { Id = "dup-id", Name = "Second" };
        service.ImportFromJson(JsonSerializer.Serialize(profile2));
        Assert.Single(service.Snapshots);
    }

    [Fact]
    public async Task ApplyPresetAsync_DelegatesToOptimizer()
    {
        var mock = BuildOptimizerMock();
        mock.Setup(o => o.ApplyProfileAsync("gaming")).ReturnsAsync(true);
        var service = new ProfileService(mock.Object);

        var result = await service.ApplyPresetAsync("gaming");

        Assert.True(result);
        mock.Verify(o => o.ApplyProfileAsync("gaming"), Times.Once);
    }
}
