using System.Collections.Concurrent;
using System.Management;

using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Optimizations;
using Optimizer.WinUI.Services.Plugins;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Thin coordinator: delegates preset data to <see cref="BuiltInPresetsProvider"/> and
/// each optimization's logic to the registered <see cref="IOptimizationHandler"/> implementations.
/// Plugin handlers discovered by <see cref="IPluginLoader"/> are merged in at construction
/// and can be refreshed at runtime via <see cref="RefreshHandlers"/>.
/// </summary>
public class WindowsOptimizerService : IWindowsOptimizerService
{
    // Built-in handlers from DI (never change after construction)
    private readonly IReadOnlyDictionary<string, IOptimizationHandler> _builtInHandlers;
    // Merged view: built-ins + enabled plugins (rebuilt by RefreshHandlers)
    private Dictionary<string, IOptimizationHandler> _handlers;
    private readonly IPluginLoader _pluginLoader;
    private readonly IUndoService _undoService;
    private readonly IElevationService _elevationService;
    private readonly ISystemMonitorService _monitorService;
    private readonly IStartupService _startupService;
    private readonly IEventBus _eventBus;
    private readonly ISettingsService? _settings;

    private readonly ConcurrentDictionary<string, SettingsProfile> _appliedProfiles = new();
    private bool _restorePointCreated;

    public WindowsOptimizerService(
        IEnumerable<IOptimizationHandler> handlers,
        IPluginLoader pluginLoader,
        IUndoService undoService,
        IElevationService elevationService,
        ISystemMonitorService monitorService,
        IStartupService startupService,
        IEventBus eventBus,
        ISettingsService? settings = null)
    {
        _builtInHandlers = handlers.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        _pluginLoader = pluginLoader;
        _undoService = undoService;
        _elevationService = elevationService;
        _monitorService = monitorService;
        _startupService = startupService;
        _eventBus = eventBus;
        _settings = settings;

        // Build initial merged dictionary
        _handlers = BuildMergedHandlers();
    }

    /// <summary>
    /// Audit C4 — machine-ownership gate (docs/MACHINE-OWNERSHIP.md): on a federated machine
    /// (FancontrolStateDir configured) Process Lasso owns power-plan switching, and
    /// OptimizePowerSettings (powercfg /setactive) re-creates the documented last-write-wins
    /// incident. Until now only the AUTO path was gated; the manual card, 6 of 10 presets,
    /// the Command Center quick action, the REST API and the scheduler all reached the handler.
    /// </summary>
    private bool FederationOwnsPowerPlans
        => !string.IsNullOrWhiteSpace(_settings?.Settings.FancontrolStateDir);

    /// <summary>
    /// Rebuilds the handler dictionary from built-ins and the current plugin set.
    /// Call after installing, enabling, disabling, or removing a plugin.
    /// </summary>
    public void RefreshHandlers()
    {
        _handlers = BuildMergedHandlers();
        EngineLog.Write($"[WindowsOptimizerService] Handler set refreshed: {_handlers.Count} total ({_builtInHandlers.Count} built-in, {_handlers.Count - _builtInHandlers.Count} plugin).");
    }

    private Dictionary<string, IOptimizationHandler> BuildMergedHandlers()
    {
        // Start with built-ins
        var merged = new Dictionary<string, IOptimizationHandler>(_builtInHandlers, StringComparer.OrdinalIgnoreCase);

        // Merge plugin handlers; built-in wins on ID collision
        foreach (var pluginHandler in _pluginLoader.CreateHandlers())
        {
            if (merged.ContainsKey(pluginHandler.Id))
            {
                EngineLog.Write($"[WindowsOptimizerService] Plugin ID '{pluginHandler.Id}' collides with a built-in handler — plugin skipped.");
                continue;
            }
            merged[pluginHandler.Id] = pluginHandler;
        }

        return merged;
    }

    // ---------------------------------------------------------------- Presets

    public IReadOnlyList<SettingsProfile> GetBuiltInPresets()
        => BuiltInPresetsProvider.GetPresets();

    // ----------------------------------------------------------- Optimizations

    public Task<IEnumerable<string>> GetAvailableOptimizationsAsync()
        => Task.FromResult<IEnumerable<string>>(_handlers.Keys.ToArray());

    public OptimizationInfo? GetOptimizationInfo(string optimizationId)
        => _handlers.TryGetValue(optimizationId, out var h) ? h.Info : null;

    public bool? IsOptimizationApplied(string optimizationId)
    {
        try
        {
            return _handlers.TryGetValue(optimizationId, out var h) ? h.IsApplied() : null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<OptimizationResult> ApplyOptimizationAsync(string optimizationId)
    {
        try
        {
            // Ownership gate at the single choke point — covers cards, presets, quick actions,
            // REST and the scheduler in one place.
            if (string.Equals(optimizationId, OptimizationIds.OptimizePowerSettings, StringComparison.OrdinalIgnoreCase)
                && FederationOwnsPowerPlans)
            {
                EngineLog.Write("OptimizePowerSettings refused: power plans are owned by Process Lasso on this machine (MACHINE-OWNERSHIP.md)");
                return new OptimizationResult
                {
                    Success = false,
                    Message = "Skipped: power plans are owned by Process Lasso on this machine.",
                    Errors = new List<string> { "Power-plan switching is federated (MACHINE-OWNERSHIP.md) — change plans in Process Lasso instead." },
                };
            }

            return await Task.Run(async () =>
            {
                if (!_handlers.TryGetValue(optimizationId, out var handler))
                {
                    return new OptimizationResult
                    {
                        Success = false,
                        Errors = new List<string> { $"Unknown optimization ID: {optimizationId}" }
                    };
                }

                // Create a one-time System Restore checkpoint before the first admin change.
                if (handler.Info.RequiresAdmin && _elevationService.IsElevated)
                    EnsureRestorePoint();

                var result = await handler.ApplyAsync(_undoService, _elevationService);

                if (result.Success)
                {
                    await _undoService.SaveAsync();
                    _eventBus.Publish(OptimizerEvent.Create(
                        OptimizerEventType.OptimizationApplied,
                        "Optimization applied",
                        handler.Info.Title,
                        new Dictionary<string, string> { ["optimizationId"] = optimizationId }));
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

    // ---------------------------------------------------------------- Profiles

    public async Task<bool> ApplyProfileAsync(string profileId)
        => (await ApplyProfileDetailedAsync(profileId)).Success;

    public async Task<ProfileApplyResult> ApplyProfileDetailedAsync(string profileId)
    {
        var agg = new ProfileApplyResult();
        try
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile ID cannot be empty");

            // Check built-in presets first, then fall back to the full presets list.
            var presets = BuiltInPresetsProvider.GetPresets();
            var profile = presets.FirstOrDefault(p => p.Id == profileId);
            if (profile == null)
            {
                agg.ProfileFound = false;
                agg.Errors.Add($"Profile {profileId} not found");
                return agg;
            }

            profile.LastAppliedAt = DateTime.UtcNow;
            _appliedProfiles[profileId] = profile;

            // Apply any explicit registry settings declared on the profile. Tag the captures with
            // the profile id so RevertProfileAsync can target exactly this profile's changes.
            foreach (var setting in profile.RegistrySettings)
            {
                try
                {
                    var root = setting.HkeyBase.Contains("LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) || setting.HkeyBase == "HKLM" ? "HKLM" : "HKCU";
                    var kind = setting.ValueKind == "REG_DWORD" ? Microsoft.Win32.RegistryValueKind.DWord : Microsoft.Win32.RegistryValueKind.String;
                    object value = kind == Microsoft.Win32.RegistryValueKind.DWord ? Convert.ToInt32(setting.ValueData) : setting.ValueData;

                    var hive = root == "HKLM" ? Microsoft.Win32.Registry.LocalMachine : Microsoft.Win32.Registry.CurrentUser;
                    _undoService.CaptureRegistry(root, setting.SubKey, setting.ValueName, setting.Description, profileId);
                    using var key = hive.CreateSubKey(setting.SubKey);
                    key.SetValue(setting.ValueName, value, kind);
                    EngineLog.Write($"Set {root}\\{setting.SubKey}\\{setting.ValueName} = {value}");
                    agg.Applied++;
                }
                catch (Exception ex) { agg.Failed++; agg.Errors.Add($"{setting.ValueName}: {ex.Message}"); }
            }

            // Run the optimization IDs bundled into this profile — aggregate each result (C6).
            foreach (var optId in profile.Optimizations)
            {
                var r = await ApplyOptimizationAsync(optId);
                if (r.Success) agg.Applied++;
                else { agg.Failed++; agg.Errors.Add(r.Errors?.Count > 0 ? string.Join("; ", r.Errors) : (r.Message ?? optId)); }
            }

            // Restore any captured startup on/off states.
            if (profile.StartupStates.Count > 0)
            {
                var current = _startupService.GetEntries();
                foreach (var state in profile.StartupStates)
                {
                    var entry = current.FirstOrDefault(e => e.Name == state.Name && e.Location == state.Location);
                    if (entry != null && entry.Enabled != state.Enabled)
                        _startupService.SetEnabled(entry, state.Enabled);
                }
            }

            await _undoService.SaveAsync();

            _eventBus.Publish(OptimizerEvent.Create(
                OptimizerEventType.ProfileApplied,
                "Profile applied",
                profile.Name,
                new Dictionary<string, string> { ["profileId"] = profileId }));

            return agg;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error applying profile: {ex.Message}");
            agg.Failed++;
            agg.Errors.Add(ex.Message);
            return agg;
        }
    }

    public async Task<bool> RevertProfileAsync(string profileId)
    {
        try
        {
            _appliedProfiles.TryRemove(profileId, out _);

            // Audit/divergence-7: revert ONLY this profile's captured changes, not the entire
            // undo stack (the old UndoAllAsync tore down every other profile + standalone
            // optimization too). Entries carry the profile id (direct registry settings) or one
            // of the profile's bundled optimization ids.
            var profile = BuiltInPresetsProvider.GetPresets().FirstOrDefault(p => p.Id == profileId);
            var scopeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { profileId };
            if (profile != null) foreach (var o in profile.Optimizations) scopeIds.Add(o);

            var mine = _undoService.Entries
                .Where(e => e.OptimizationId != null && scopeIds.Contains(e.OptimizationId))
                .Reverse()   // newest first
                .ToList();

            if (mine.Count == 0)
            {
                EngineLog.Write($"RevertProfile {profileId}: no scoped undo entries found");
                return false;
            }
            foreach (var e in mine) await _undoService.UndoAsync(e);
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
        => _monitorService.CollectSnapshot();

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

    // --------------------------------------------------------------- Undo pass-through

    public bool IsElevated => _elevationService.IsElevated;
    public int PendingUndoCount => _undoService.Count;
    public IReadOnlyList<UndoEntry> GetUndoEntries() => _undoService.Entries;
    public Task<bool> UndoEntryAsync(UndoEntry entry) => _undoService.UndoAsync(entry);
    public async Task<int> UndoAllOptimizationsAsync() => await _undoService.UndoAllAsync();

    // ------------------------------------------------------- System Restore Point

    /// <summary>
    /// Creates a System Restore checkpoint (once per app run) before the first system-wide change.
    /// Best-effort: silently no-ops if System Protection is disabled or Windows rate-limits the request.
    /// </summary>
    private void EnsureRestorePoint()
    {
        if (_restorePointCreated) return;
        _restorePointCreated = true;

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
}
