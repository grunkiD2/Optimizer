using System.Collections.Concurrent;
using System.Diagnostics;
using System.Management;

using Microsoft.Win32;

using Optimizer.WinUI.Models;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.Services;

public class WindowsOptimizerService : IWindowsOptimizerService
{
    // High Performance power scheme (built-in, stable GUID across Windows versions).
    private const string HighPerformanceSchemeGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private readonly ISystemMonitorService _monitorService;
    private readonly IUndoService _undoService;
    private readonly IElevationService _elevationService;
    private readonly IStartupService _startupService;

    private readonly ConcurrentDictionary<string, SettingsProfile> _appliedProfiles = new();

    public WindowsOptimizerService(
        ISystemMonitorService monitorService,
        IUndoService undoService,
        IElevationService elevationService,
        IStartupService startupService)
    {
        _monitorService = monitorService;
        _undoService = undoService;
        _elevationService = elevationService;
        _startupService = startupService;
    }

    public IReadOnlyList<SettingsProfile> GetBuiltInPresets() => BuiltInPresets
        .Select(ClonePreset)
        .ToList();

    private static SettingsProfile ClonePreset(SettingsProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        ProfileType = p.ProfileType,
        Optimizations = new List<string>(p.Optimizations)
    };

    private static readonly SettingsProfile[] BuiltInPresets =
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

    public bool IsElevated => _elevationService.IsElevated;

    public int PendingUndoCount => _undoService.Count;

    public IReadOnlyList<UndoEntry> GetUndoEntries() => _undoService.Entries;

    public Task<bool> UndoEntryAsync(UndoEntry entry) => _undoService.UndoAsync(entry);

    public async Task<int> UndoAllOptimizationsAsync()
    {
        var restored = await _undoService.UndoAllAsync();
        return restored;
    }

    // ---------------------------------------------------------------- Profiles

    public bool? IsOptimizationApplied(string optimizationId)
    {
        try
        {
            switch (optimizationId.ToLowerInvariant())
            {
                case "disablebackgroundapps":
                    return ReadHkcu(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == "1";
                case "disableanimations":
                    return ReadHkcu(@"Control Panel\Desktop\WindowMetrics", "MinAnimate") == "0";
                case "disablevisualeffects":
                    return ReadHkcu(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting") == "2";
                case "optimizepowersettings":
                    return string.Equals(GetActivePowerSchemeGuid(), HighPerformanceSchemeGuid, StringComparison.OrdinalIgnoreCase);
                case "disabletelemetry":
                    return ReadHklm(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry") == "0";
                case "disableconsumerfeatures":
                    return ReadHklm(@"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures") == "1";
                case "optimizenetworksettings":
                    return ReadHklm(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness") == "0";
                default:
                    return null; // not statically determinable (file ops, startup, page file, etc.)
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadHkcu(string subKey, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        return key?.GetValue(name)?.ToString();
    }

    private static string? ReadHklm(string subKey, string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey);
        return key?.GetValue(name)?.ToString();
    }

    public async Task<bool> ApplyProfileAsync(string profileId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID cannot be empty");

            var profile = BuiltInPresets.FirstOrDefault(p => p.Id == profileId)
                ?? throw new KeyNotFoundException($"Profile {profileId} not found");

            profile.LastAppliedAt = DateTime.UtcNow;
            _appliedProfiles[profileId] = profile;

            // Apply the registry settings declared on the profile (each captured for undo).
            foreach (var setting in profile.RegistrySettings)
            {
                var root = setting.HkeyBase.Contains("LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || setting.HkeyBase == "HKLM" ? "HKLM" : "HKCU";
                var kind = setting.ValueKind == "REG_DWORD" ? RegistryValueKind.DWord : RegistryValueKind.String;
                object value = kind == RegistryValueKind.DWord ? Convert.ToInt32(setting.ValueData) : setting.ValueData;
                SetRegistryValue(root, setting.SubKey, setting.ValueName, value, kind, $"Profile '{profile.Name}': {setting.Description}");
            }

            // Run any optimizations bundled into the profile (reuses the tested apply + undo path).
            foreach (var optimizationId in profile.Optimizations)
            {
                await ApplyOptimizationAsync(optimizationId);
            }

            // Restore captured startup on/off states (skips entries not present on this machine).
            if (profile.StartupStates.Count > 0)
            {
                var current = _startupService.GetEntries();
                foreach (var state in profile.StartupStates)
                {
                    var entry = current.FirstOrDefault(e => e.Name == state.Name && e.Location == state.Location);
                    if (entry != null && entry.Enabled != state.Enabled)
                    {
                        _startupService.SetEnabled(entry, state.Enabled);
                    }
                }
            }

            await _undoService.SaveAsync();
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error applying profile: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RevertProfileAsync(string profileId)
    {
        try
        {
            _appliedProfiles.TryRemove(profileId, out _);
            await _undoService.UndoAllAsync();
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error reverting profile: {ex.Message}");
            return false;
        }
    }

    // ------------------------------------------------------------- Monitoring

    public SystemResource GetCurrentResourceUsage()
    {
        // Delegate to the monitor service so the dashboard gets the full metric set
        // (CPU, memory, virtual memory, GPU, temps, disk, network) from one code path.
        return _monitorService.CollectSnapshot();
    }

    public async Task<IEnumerable<SystemResource>> GetResourceHistoryAsync(int sampleCount)
    {
        try
        {
            return await _monitorService.GetResourceHistoryAsync(sampleCount);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error getting resource history: {ex.Message}");
            return Enumerable.Empty<SystemResource>();
        }
    }

    // ----------------------------------------------------------- Optimizations

    public Task<IEnumerable<string>> GetAvailableOptimizationsAsync()
    {
        return Task.FromResult<IEnumerable<string>>(Catalog.Keys.ToArray());
    }

    public OptimizationInfo? GetOptimizationInfo(string optimizationId)
    {
        return Catalog.TryGetValue(optimizationId, out var info) ? info : null;
    }

    private static readonly Dictionary<string, OptimizationInfo> Catalog = new()
    {
        ["DisableBackgroundApps"] = new OptimizationInfo
        {
            Id = "DisableBackgroundApps",
            Title = "Disable background apps",
            Summary = "Stops Microsoft Store / UWP apps from running in the background for the current user.",
            Changes = { @"Sets HKCU\…\BackgroundAccessApplications\GlobalUserDisabled = 1 (DWORD)" },
            Pros = { "Frees CPU/RAM and reduces battery drain from idle apps", "Takes effect immediately, no restart" },
            Cons = { "Live tiles, push notifications and some sync for Store apps may stop", "Does not affect classic desktop (Win32) programs" },
            Recommendation = "Safe and recommended on laptops and low-RAM machines. Reversible at any time.",
            RequiresAdmin = false,
            Reversible = true
        },
        ["DisableAnimations"] = new OptimizationInfo
        {
            Id = "DisableAnimations",
            Title = "Disable window & taskbar animations",
            Summary = "Turns off window minimize/maximize and taskbar animations for snappier UI.",
            Changes =
            {
                @"Sets HKCU\Control Panel\Desktop\WindowMetrics\MinAnimate = 0",
                @"Sets HKCU\…\Explorer\Advanced\TaskbarAnimations = 0 (DWORD)"
            },
            Pros = { "UI feels faster and more responsive", "Helps on older GPUs / remote desktop sessions" },
            Cons = { "Transitions are abrupt rather than smooth (cosmetic)" },
            Recommendation = "Great low-risk win for perceived speed. Sign out/in for a fully consistent effect.",
            RequiresAdmin = false,
            Reversible = true,
            RequiresRestart = true
        },
        ["DisableVisualEffects"] = new OptimizationInfo
        {
            Id = "DisableVisualEffects",
            Title = "Adjust visual effects for best performance",
            Summary = "Sets Windows performance options to 'Adjust for best performance'.",
            Changes = { @"Sets HKCU\…\Explorer\VisualEffects\VisualFXSetting = 2 (DWORD)" },
            Pros = { "Disables shadows, fades and other effects to reduce GPU/CPU load" },
            Cons = { "Windows looks flatter/plainer", "Some users find disabled font smoothing harsh — can be re-enabled separately" },
            Recommendation = "Recommended on low-end hardware. Pairs well with 'Disable animations'.",
            SuggestedImplementation = "For finer control, set individual UserPreferencesMask bits instead of the global 'best performance' switch.",
            RequiresAdmin = false,
            Reversible = true,
            RequiresRestart = true
        },
        ["OptimizePowerSettings"] = new OptimizationInfo
        {
            Id = "OptimizePowerSettings",
            Title = "Switch to High Performance power plan",
            Summary = "Activates the built-in High Performance power scheme via powercfg.",
            Changes = { "Runs: powercfg /setactive 8c5e7fda-… (High Performance)", "Your previous active scheme is recorded for undo" },
            Pros = { "Keeps CPU at higher clocks, reduces latency/stutter", "Good for desktops and gaming", "The one tweak with a large, measured impact (Intel Arrow Lake saw ~55–67% single-core drops off 'Best performance'; Intel recommends it)" },
            Cons = { "Higher power draw and heat", "Reduces battery life on laptops" },
            Recommendation = "★ Highest-impact optimization here (evidence-backed). Recommended on desktops. On laptops, prefer 'Balanced' unless plugged in.",
            SuggestedImplementation = "Consider the 'Ultimate Performance' plan on workstations (powercfg -duplicatescheme e9a42b02-…).",
            RequiresAdmin = false,
            Reversible = true
        },
        ["ClearTemporaryFiles"] = new OptimizationInfo
        {
            Id = "ClearTemporaryFiles",
            Title = "Clear temporary files",
            Summary = "Deletes files in your user TEMP folder that aren't currently in use.",
            Changes = { @"Deletes top-level files in %TEMP% (" + @"e.g. C:\Users\…\AppData\Local\Temp)" },
            Pros = { "Frees disk space", "Removes stale installer/cache leftovers" },
            Cons = { "Cannot be undone", "Skips files locked by running programs" },
            Recommendation = "Safe to run periodically. Close apps first to clear more. NOT reversible — no undo entry is created.",
            SuggestedImplementation = "For a deeper clean, also target the Windows TEMP and Windows Update download caches (requires admin).",
            RequiresAdmin = false,
            Reversible = false
        },
        ["DisableStartupPrograms"] = new OptimizationInfo
        {
            Id = "DisableStartupPrograms",
            Title = "Disable per-user startup programs",
            Summary = "Removes programs that auto-start at sign-in for the current user.",
            Changes = { @"Removes all values under HKCU\…\CurrentVersion\Run (each captured for undo)" },
            Pros = { "Faster sign-in and lower idle resource use" },
            Cons = { "Disables ALL per-user Run entries indiscriminately", "Does not touch machine-wide (HKLM) or Startup-folder items" },
            Recommendation = "Prefer the Startup tab for per-item control. This bulk action removes everything at once; use Undo to restore if needed.",
            SuggestedImplementation = "The Startup tab lets you toggle individual entries (HKCU + HKLM Run) one by one instead of this all-or-nothing action.",
            RequiresAdmin = false,
            Reversible = true
        },
        ["AdjustPageFileSize"] = new OptimizationInfo
        {
            Id = "AdjustPageFileSize",
            Title = "Set page file to system-managed",
            Summary = "Lets Windows manage the page file size on the system drive (recommended default).",
            Changes = { @"Sets HKLM\…\Memory Management\PagingFiles to '<SystemDrive>\pagefile.sys 0 0' (system-managed)" },
            Pros = { "Avoids out-of-memory errors from a too-small fixed page file", "Good general-purpose default" },
            Cons = { "Requires administrator", "Needs a restart to take effect", "If 'Automatically manage' is on in System Properties it must be turned off first" },
            Recommendation = "Recommended if your page file was previously set too small or disabled. Restart afterwards.",
            SuggestedImplementation = "For SSD systems with lots of RAM, a fixed size (e.g. 1.5× RAM) can reduce fragmentation; expose size as an option.",
            RequiresAdmin = true,
            Reversible = true,
            RequiresRestart = true
        },
        ["OptimizeNetworkSettings"] = new OptimizationInfo
        {
            Id = "OptimizeNetworkSettings",
            Title = "Disable network throttling",
            Summary = "Removes the multimedia network throttle and maximizes system responsiveness.",
            Changes =
            {
                @"Sets HKLM\…\Multimedia\SystemProfile\NetworkThrottlingIndex = 0xFFFFFFFF",
                @"Sets HKLM\…\Multimedia\SystemProfile\SystemResponsiveness = 0 (DWORD)"
            },
            Pros = { "Lifts the MMCSS packet throttle that can cap gigabit during media playback + large transfers" },
            Cons = { "Requires administrator", "Benefit is narrow — only matters when streaming media during a big network transfer; no effect otherwise", "Windows clamps SystemResponsiveness below 10 up to 20, so '0' is not applied as written", "A restart is recommended" },
            Recommendation = "Niche: mainly helps the specific 'media playing while transferring large files on gigabit' case. Little to no benefit for general use. Reversible via Undo.",
            RequiresAdmin = true,
            Reversible = true,
            RequiresRestart = true
        },
        ["FlushDnsCache"] = new OptimizationInfo
        {
            Id = "FlushDnsCache",
            Title = "Flush DNS cache",
            Summary = "Clears the local DNS resolver cache to fix stale name-resolution issues.",
            Changes = { "Runs: ipconfig /flushdns" },
            Pros = { "Resolves 'can't reach site' issues caused by stale/poisoned DNS entries", "Instant, no restart" },
            Cons = { "First lookups after the flush are marginally slower while the cache repopulates" },
            Recommendation = "Safe any time. A one-shot maintenance action — there's nothing to undo.",
            RequiresAdmin = false,
            Reversible = false
        },
        ["DisableTelemetry"] = new OptimizationInfo
        {
            Id = "DisableTelemetry",
            Title = "Minimize telemetry",
            Summary = "Sets the diagnostic-data policy to its lowest level.",
            Changes = { @"Sets HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection\AllowTelemetry = 0 (DWORD)" },
            Pros = { "Reduces background diagnostic uploads", "Improves privacy" },
            Cons = { "Requires administrator", "Home/Pro enforce a minimum 'Required' level", "A restart may be needed" },
            Recommendation = "Reversible via Undo. On Home/Pro the effective floor may be higher than Off.",
            RequiresAdmin = true,
            Reversible = true,
            RequiresRestart = true
        },
        ["DisableConsumerFeatures"] = new OptimizationInfo
        {
            Id = "DisableConsumerFeatures",
            Title = "Disable suggested apps & ads",
            Summary = "Stops Windows from auto-installing suggested apps and showing consumer promotions.",
            Changes = { @"Sets HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent\DisableWindowsConsumerFeatures = 1 (DWORD)" },
            Pros = { "No more auto-installed 'recommended' apps", "Cleaner Start menu" },
            Cons = { "Requires administrator", "Mainly effective on Pro/Enterprise" },
            Recommendation = "Recommended for a cleaner setup. Reversible via Undo.",
            RequiresAdmin = true,
            Reversible = true
        },
        ["DisableHibernation"] = new OptimizationInfo
        {
            Id = "DisableHibernation",
            Title = "Disable hibernation",
            Summary = "Turns off hibernation and frees the hiberfil.sys file (often several GB).",
            Changes = { "Runs: powercfg /hibernate off" },
            Pros = { "Frees disk space equal to a large fraction of your RAM", "Removes hiberfil.sys" },
            Cons = { "Requires administrator", "Disables Hibernate and may disable Fast Startup", "Not part of Undo" },
            Recommendation = "Good on desktops with limited SSD space. Re-enable with: powercfg /hibernate on",
            RequiresAdmin = true,
            Reversible = false
        },
        ["ClearWindowsUpdateCache"] = new OptimizationInfo
        {
            Id = "ClearWindowsUpdateCache",
            Title = "Clear Windows Update cache",
            Summary = "Stops the update service, deletes downloaded update files, then restarts it.",
            Changes = { "Stops wuauserv", @"Deletes %WinDir%\SoftwareDistribution\Download\*", "Starts wuauserv" },
            Pros = { "Frees disk space", "Fixes stuck/corrupted update downloads" },
            Cons = { "Requires administrator", "Cannot be undone", "Windows re-downloads pending updates" },
            Recommendation = "Use when updates are stuck or to reclaim space. Not reversible.",
            RequiresAdmin = true,
            Reversible = false
        }
    };

    public async Task<OptimizationResult> ApplyOptimizationAsync(string optimizationId)
    {
        try
        {
            return await Task.Run(async () =>
            {
                var result = new OptimizationResult { Success = true };

                // For system-wide (admin) changes, create a one-time System Restore checkpoint first.
                if (GetOptimizationInfo(optimizationId)?.RequiresAdmin == true && _elevationService.IsElevated)
                {
                    EnsureRestorePoint();
                }

                switch (optimizationId.ToLowerInvariant())
                {
                    case "disablebackgroundapps":
                        SetRegistryValue("HKCU",
                            @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications",
                            "GlobalUserDisabled", 1, RegistryValueKind.DWord,
                            "Disable background apps");
                        result.Message = "Background apps disabled for the current user.";
                        break;

                    case "disableanimations":
                        SetRegistryValue("HKCU", @"Control Panel\Desktop\WindowMetrics",
                            "MinAnimate", "0", RegistryValueKind.String, "Disable window animations");
                        SetRegistryValue("HKCU",
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                            "TaskbarAnimations", 0, RegistryValueKind.DWord, "Disable taskbar animations");
                        result.Message = "Window and taskbar animations disabled.";
                        break;

                    case "disablevisualeffects":
                        SetRegistryValue("HKCU",
                            @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
                            "VisualFXSetting", 2, RegistryValueKind.DWord,
                            "Adjust visual effects for best performance");
                        result.Message = "Visual effects set to 'best performance'.";
                        break;

                    case "optimizepowersettings":
                        result = OptimizePowerSettings();
                        break;

                    case "cleartemporaryfiles":
                        var (deleted, freedBytes) = ClearTemporaryFiles();
                        result.Message = $"Cleared {deleted} temporary file(s), freed {freedBytes / 1024 / 1024} MB.";
                        result.Warnings.Add("Deleting temporary files cannot be undone.");
                        break;

                    case "disablestartupprograms":
                        result = DisableStartupPrograms();
                        break;

                    case "adjustpagefilesize":
                        result = SetSystemManagedPageFile();
                        break;

                    case "optimizenetworksettings":
                        result = OptimizeNetworkSettings();
                        break;

                    case "flushdnscache":
                        result = FlushDnsCache();
                        break;

                    case "disabletelemetry":
                        result = DisableTelemetry();
                        break;

                    case "disableconsumerfeatures":
                        result = DisableConsumerFeatures();
                        break;

                    case "disablehibernation":
                        result = DisableHibernation();
                        break;

                    case "clearwindowsupdatecache":
                        result = ClearWindowsUpdateCache();
                        break;

                    default:
                        result.Success = false;
                        result.Errors.Add($"Unknown optimization ID: {optimizationId}");
                        break;
                }

                if (result.Success)
                {
                    await _undoService.SaveAsync();
                }

                return result;
            });
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error applying optimization: {ex.Message}");
            return new OptimizationResult
            {
                Success = false,
                Message = "Optimization failed",
                Errors = new List<string> { ex.Message }
            };
        }
    }

    private OptimizationResult OptimizePowerSettings()
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            var current = GetActivePowerSchemeGuid();
            if (!string.IsNullOrEmpty(current))
            {
                _undoService.CapturePowerScheme(current, "Restore previous power scheme");
            }

            UndoService.RunPowerCfg($"/setactive {HighPerformanceSchemeGuid}");
            result.Message = "Switched to the High Performance power scheme.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change power scheme: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Removes per-user (HKCU) Run entries so they no longer launch at sign-in.
    /// Each removed value is captured first so it can be restored via Undo.
    /// </summary>
    private OptimizationResult DisableStartupPrograms()
    {
        var result = new OptimizationResult { Success = true };
        const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            string[] names;
            using (var key = Registry.CurrentUser.OpenSubKey(runKey, writable: false))
            {
                names = key?.GetValueNames().Where(n => !string.IsNullOrEmpty(n)).ToArray()
                        ?? Array.Empty<string>();
            }

            foreach (var name in names)
            {
                DeleteRegistryValue("HKCU", runKey, name, $"Startup program '{name}'");
            }

            result.Message = names.Length == 0
                ? "No per-user startup programs were found."
                : $"Disabled {names.Length} per-user startup program(s).";
            if (names.Length > 0)
            {
                result.Warnings.Add("Only per-user (HKCU) entries were removed; machine-wide and Startup-folder items are unchanged. Use Undo to restore them.");
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not modify startup programs: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Sets the system-drive page file to system-managed size ("0 0"). Writes HKLM, so it
    /// requires elevation, and a restart is needed before Windows honors the change.
    /// </summary>
    private OptimizationResult SetSystemManagedPageFile()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("Page-file changes write to HKLM and require running as administrator.");
            return result;
        }

        try
        {
            var systemDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
            var pagingFiles = new[] { $@"{systemDrive}\pagefile.sys 0 0" }; // "0 0" => system-managed size
            SetRegistryValue("HKLM",
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
                "PagingFiles", pagingFiles, RegistryValueKind.MultiString,
                "Set page file to system-managed");

            result.Message = $"Page file set to system-managed on {systemDrive}.";
            result.Warnings.Add("Restart required. If 'Automatically manage paging file size' is on in System Properties, turn it off for this to take effect.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change page-file settings: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Disables network throttling and maximizes multimedia/network responsiveness via the
    /// SystemProfile keys. Writes HKLM, so it requires elevation.
    /// </summary>
    private OptimizationResult OptimizeNetworkSettings()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("Network tuning writes to HKLM and requires running as administrator.");
            return result;
        }

        try
        {
            const string profileKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
            SetRegistryValue("HKLM", profileKey, "NetworkThrottlingIndex",
                unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord, "Disable network throttling");
            SetRegistryValue("HKLM", profileKey, "SystemResponsiveness",
                0, RegistryValueKind.DWord, "Maximize multimedia/network responsiveness");

            result.Message = "Disabled network throttling and maximized system responsiveness.";
            result.Warnings.Add("A restart may be required for these changes to fully apply.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change network settings: {ex.Message}");
        }
        return result;
    }

    private OptimizationResult FlushDnsCache()
    {
        var result = new OptimizationResult { Success = true };
        try
        {
            RunProcess("ipconfig", "/flushdns");
            result.Message = "DNS resolver cache flushed.";
            result.Warnings.Add("This is a one-time action; the cache rebuilds automatically (nothing to undo).");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not flush DNS cache: {ex.Message}");
        }
        return result;
    }

    private OptimizationResult DisableTelemetry()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("Telemetry policy is written to HKLM and requires running as administrator.");
            return result;
        }
        try
        {
            SetRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection",
                "AllowTelemetry", 0, RegistryValueKind.DWord, "Set diagnostic data to Security/Off");
            result.Message = "Telemetry policy set to the minimum level.";
            result.Warnings.Add("Some editions enforce a higher minimum; a restart may be needed.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not set telemetry policy: {ex.Message}");
        }
        return result;
    }

    private OptimizationResult DisableConsumerFeatures()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("This policy is written to HKLM and requires running as administrator.");
            return result;
        }
        try
        {
            SetRegistryValue("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent",
                "DisableWindowsConsumerFeatures", 1, RegistryValueKind.DWord, "Disable Windows consumer features (suggested apps/ads)");
            result.Message = "Disabled Windows consumer features (auto-installed suggested apps and ads).";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not change consumer-features policy: {ex.Message}");
        }
        return result;
    }

    private OptimizationResult DisableHibernation()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("Toggling hibernation requires running as administrator.");
            return result;
        }
        try
        {
            RunProcess("powercfg", "/hibernate off");
            result.Message = "Hibernation disabled (frees the hiberfil.sys disk space).";
            result.Warnings.Add("Not part of Undo. Re-enable any time with: powercfg /hibernate on");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not disable hibernation: {ex.Message}");
        }
        return result;
    }

    private OptimizationResult ClearWindowsUpdateCache()
    {
        var result = new OptimizationResult { Success = true };
        if (!_elevationService.IsElevated)
        {
            result.Success = false;
            result.Message = "Not applied.";
            result.Errors.Add("Clearing the Windows Update cache requires running as administrator.");
            return result;
        }
        try
        {
            RunProcess("net", "stop wuauserv");
            var download = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
            long freed = 0;
            if (Directory.Exists(download))
            {
                foreach (var file in Directory.GetFiles(download, "*", SearchOption.AllDirectories))
                {
                    try { freed += new FileInfo(file).Length; File.Delete(file); } catch { }
                }
            }
            RunProcess("net", "start wuauserv");
            result.Message = $"Cleared the Windows Update download cache (~{freed / 1024 / 1024} MB).";
            result.Warnings.Add("Cannot be undone; Windows will re-download updates as needed.");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Could not clear update cache: {ex.Message}");
        }
        return result;
    }

    private bool _restorePointCreated;

    /// <summary>
    /// Creates a System Restore checkpoint (once per app run) before the first system-wide change,
    /// as a safety net beyond the per-change undo log. Best-effort: silently no-ops if System
    /// Protection is disabled or Windows rate-limits the request.
    /// </summary>
    private void EnsureRestorePoint()
    {
        if (_restorePointCreated)
        {
            return;
        }
        _restorePointCreated = true; // attempt only once per run regardless of outcome

        try
        {
            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            var inParams = cls.GetMethodParameters("CreateRestorePoint");
            inParams["Description"] = "Optimizer: before applying system changes";
            inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
            inParams["EventType"] = 100;        // BEGIN_SYSTEM_CHANGE
            var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
            var ret = outParams?["ReturnValue"] != null ? Convert.ToUInt32(outParams["ReturnValue"]) : 0;
            EngineLog.Write(ret == 0
                ? "System restore point created."
                : $"Restore point not created (code {ret}; System Protection may be off or rate-limited).");
        }
        catch (Exception ex)
        {
            EngineLog.Error("Could not create system restore point", ex);
        }
    }

    private static void RunProcess(string fileName, string arguments)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(15000);
    }

    // --------------------------------------------------------- Registry helper

    private void SetRegistryValue(string root, string subKey, string valueName, object value, RegistryValueKind kind, string description)
    {
        // Capture the prior value before mutating so the change is reversible.
        _undoService.CaptureRegistry(root, subKey, valueName, description);

        var hive = root == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = hive.CreateSubKey(subKey);
        key.SetValue(valueName, value, kind);
        EngineLog.Write($"Set {root}\\{subKey}\\{valueName} = {value}");
    }

    private void DeleteRegistryValue(string root, string subKey, string valueName, string description)
    {
        // Capture the prior value so the deletion can be undone.
        _undoService.CaptureRegistry(root, subKey, valueName, description);

        var hive = root == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
        using var key = hive.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
        EngineLog.Write($"Deleted {root}\\{subKey}\\{valueName}");
    }

    private static string GetActivePowerSchemeGuid()
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = Process.Start(psi);
            if (process == null) return string.Empty;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Output: "Power Scheme GUID: <guid>  (Name)"
            var match = System.Text.RegularExpressions.Regex.Match(output, "[0-9a-fA-F-]{36}");
            return match.Success ? match.Value : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static (int deleted, long freedBytes) ClearTemporaryFiles()
    {
        var deleted = 0;
        long freed = 0;
        try
        {
            var tempPath = Path.GetTempPath();
            if (Directory.Exists(tempPath))
            {
                foreach (var file in Directory.GetFiles(tempPath, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var size = new FileInfo(file).Length;
                        File.Delete(file);
                        deleted++;
                        freed += size;
                    }
                    catch { /* file in use — skip */ }
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error clearing temporary files: {ex.Message}");
        }
        return (deleted, freed);
    }

}
