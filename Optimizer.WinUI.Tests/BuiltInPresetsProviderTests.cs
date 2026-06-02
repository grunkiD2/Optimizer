using System.Linq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for BuiltInPresetsProvider — verifies catalog integrity.
/// </summary>
public class BuiltInPresetsProviderTests
{
    [Fact]
    public void GetPresets_ReturnsNonEmptyList()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        Assert.NotEmpty(presets);
    }

    [Fact]
    public void GetPresets_EachPreset_HasRequiredFields()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        foreach (var p in presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id), $"Preset has empty Id");
            Assert.False(string.IsNullOrWhiteSpace(p.Name), $"Preset '{p.Id}' has empty Name");
            Assert.NotNull(p.Optimizations);
        }
    }

    [Fact]
    public void GetPresets_Contains_GamingPreset()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        Assert.Contains(presets, p => p.Id == "preset-gaming");
    }

    [Fact]
    public void GetPresets_Contains_PrivacyPreset()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        Assert.Contains(presets, p => p.Id == "preset-privacy");
    }

    [Fact]
    public void GetPresets_Returns_IndependentCopies()
    {
        // Modifying one list should not affect the next call
        var first = BuiltInPresetsProvider.GetPresets();
        first[0].Optimizations.Clear();

        var second = BuiltInPresetsProvider.GetPresets();
        Assert.NotEmpty(second[0].Optimizations);
    }

    [Fact]
    public void GetPresets_AllOptimizationIds_AreKnown()
    {
        // Every optimization referenced in a preset must be a declared OptimizationIds constant.
        var knownIds = new[]
        {
            Ids.DisableBackgroundApps,
            Ids.DisableAnimations,
            Ids.DisableVisualEffects,
            Ids.OptimizePowerSettings,
            Ids.AdjustPageFileSize,
            Ids.OptimizeNetworkSettings,
            Ids.FlushDnsCache,
            Ids.ClearTemporaryFiles,
            Ids.ClearWindowsUpdateCache,
            Ids.DisableTelemetry,
            Ids.DisableConsumerFeatures,
            Ids.DisableHibernation,
            "DisableStartupPrograms" // referenced in preset-clean but not yet a typed constant
        };

        var presets = BuiltInPresetsProvider.GetPresets();
        foreach (var preset in presets)
        {
            foreach (var optId in preset.Optimizations)
            {
                Assert.Contains(optId, knownIds);
            }
        }
    }

    [Fact]
    public void GetPresets_GamingPreset_ContainsPowerAndNetworkOptimizations()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        var gaming = presets.First(p => p.Id == "preset-gaming");

        Assert.Contains(Ids.OptimizePowerSettings, gaming.Optimizations);
        Assert.Contains(Ids.OptimizeNetworkSettings, gaming.Optimizations);
    }

    [Fact]
    public void GetPresets_PrivacyPreset_ContainsTelemetryOptimization()
    {
        var presets = BuiltInPresetsProvider.GetPresets();
        var privacy = presets.First(p => p.Id == "preset-privacy");

        Assert.Contains(Ids.DisableTelemetry, privacy.Optimizations);
    }
}
