using Optimizer.WinUI.Models;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Single source of truth for the built-in preset catalog.
/// Keeping preset data out of <see cref="WindowsOptimizerService"/> lets the coordinator
/// stay thin while keeping preset definitions easy to find and extend.
/// </summary>
public static class BuiltInPresetsProvider
{
    public static IReadOnlyList<SettingsProfile> GetPresets()
        => _presets.Select(ClonePreset).ToList();

    private static SettingsProfile ClonePreset(SettingsProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        ProfileType = p.ProfileType,
        Optimizations = new List<string>(p.Optimizations)
    };

    private static readonly SettingsProfile[] _presets =
    {
        new()
        {
            Id = "preset-gaming", Name = "Gaming", ProfileType = ProfileType.Gaming,
            Description = "Maximum responsiveness for games: high-performance power, no animations, low background load.",
            Optimizations = { Ids.OptimizePowerSettings, Ids.DisableAnimations, Ids.DisableVisualEffects, Ids.DisableBackgroundApps, Ids.OptimizeNetworkSettings }
        },
        new()
        {
            Id = "preset-productivity", Name = "Productivity", ProfileType = ProfileType.Productivity,
            Description = "Snappy UI with fewer background distractions, keeping the desktop looking normal.",
            Optimizations = { Ids.DisableAnimations, Ids.DisableBackgroundApps }
        },
        new()
        {
            Id = "preset-battery", Name = "Battery Saver", ProfileType = ProfileType.BatterySaver,
            Description = "Reduce background and visual load to extend battery life on laptops.",
            Optimizations = { Ids.DisableBackgroundApps, Ids.DisableVisualEffects, Ids.DisableAnimations }
        },
        new()
        {
            Id = "preset-performance", Name = "Maximum Performance", ProfileType = ProfileType.Performance,
            Description = "Everything tuned for raw speed. Some items need administrator rights and a restart.",
            Optimizations = { Ids.OptimizePowerSettings, Ids.DisableVisualEffects, Ids.DisableAnimations, Ids.DisableBackgroundApps, Ids.AdjustPageFileSize, Ids.OptimizeNetworkSettings }
        },
        new()
        {
            Id = "preset-clean", Name = "Clean & Light", ProfileType = ProfileType.Custom,
            Description = "Free disk space and trim per-user startup programs.",
            Optimizations = { Ids.ClearTemporaryFiles, Ids.DisableBackgroundApps, "DisableStartupPrograms" }
        },
        new()
        {
            Id = "preset-streaming", Name = "Streaming", ProfileType = ProfileType.Custom,
            Description = "Optimized for live streaming with OBS/XSplit",
            Optimizations = { Ids.DisableBackgroundApps, Ids.DisableAnimations, Ids.OptimizePowerSettings, Ids.OptimizeNetworkSettings }
        },
        new()
        {
            Id = "preset-video-editing", Name = "Video Editing", ProfileType = ProfileType.Performance,
            Description = "Maximum resources for video rendering workloads",
            Optimizations = { Ids.DisableBackgroundApps, Ids.OptimizePowerSettings, Ids.AdjustPageFileSize, Ids.DisableVisualEffects }
        },
        new()
        {
            Id = "preset-music", Name = "Music Production", ProfileType = ProfileType.Custom,
            Description = "Low-latency audio with minimal interruptions",
            Optimizations = { Ids.DisableBackgroundApps, Ids.OptimizePowerSettings, Ids.DisableAnimations, Ids.DisableTelemetry }
        },
        new()
        {
            Id = "preset-privacy", Name = "Privacy Maximum", ProfileType = ProfileType.Custom,
            Description = "All telemetry, consumer features, and tracking disabled",
            Optimizations = { Ids.DisableTelemetry, Ids.DisableConsumerFeatures }
        },
        new()
        {
            Id = "preset-quiet", Name = "Quiet PC", ProfileType = ProfileType.BatterySaver,
            Description = "Lower power for reduced fan noise and thermals",
            Optimizations = { Ids.OptimizePowerSettings, Ids.DisableBackgroundApps }
        }
    };
}
