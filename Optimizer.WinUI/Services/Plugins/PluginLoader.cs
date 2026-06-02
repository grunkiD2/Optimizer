using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services.Events;
using Optimizer.WinUI.Services.Optimizations;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Default implementation of <see cref="IPluginLoader"/>.
///
/// Plugins folder  : %LocalAppData%\Optimizer\plugins\
/// Enabled-state   : %LocalAppData%\Optimizer\plugin-state.json  (Dictionary&lt;string, bool&gt;)
///
/// A plugin with parse/validation errors is silently skipped (not surfaced as a LoadedPlugin).
/// A plugin with permission violations is loaded but flagged and force-disabled so it never
/// produces a handler.
/// </summary>
public sealed class PluginLoader : IPluginLoader
{
    private readonly IManifestParser _parser;
    private readonly IDeclarativeChangeExecutor _executor;
    private readonly IEventBus? _eventBus;
    private readonly string _pluginsFolder;
    private readonly string _stateFilePath;

    // Guard for thread-safe reload (callers should not be hot-looping, but be safe)
    private readonly object _lock = new();
    private List<LoadedPlugin> _loaded = [];

    // ── Public constructors ───────────────────────────────────────────────────

    /// <summary>Production constructor — uses the standard AppData\Optimizer\plugins folder.</summary>
    public PluginLoader(IManifestParser parser, IDeclarativeChangeExecutor executor, IEventBus eventBus)
        : this(parser, executor,
               Path.Combine(AppPaths.AppDataFolder, "plugins"),
               AppPaths.GetDataFile("plugin-state.json"),
               eventBus)
    { }

    /// <summary>
    /// Testability constructor — allows injection of arbitrary folder paths so unit tests
    /// can use a temp directory without touching AppData.
    /// </summary>
    internal PluginLoader(IManifestParser parser, IDeclarativeChangeExecutor executor,
                          string pluginsFolder, string stateFilePath,
                          IEventBus? eventBus = null)
    {
        _parser        = parser;
        _executor      = executor;
        _pluginsFolder = pluginsFolder;
        _stateFilePath = stateFilePath;
        _eventBus      = eventBus;

        EnsureFolder();
        Reload();
    }

    // ── IPluginLoader ─────────────────────────────────────────────────────────

    public string PluginsFolder => _pluginsFolder;

    public IReadOnlyList<LoadedPlugin> LoadedPlugins
    {
        get { lock (_lock) return _loaded; }
    }

    public void Reload()
    {
        EnsureFolder();

        var stateMap = LoadState();
        var manifests = new List<LoadedPlugin>();

        // Scan for all manifest files
        var files = Directory.EnumerateFiles(_pluginsFolder, "*.*")
            .Where(f =>
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                return ext is ".yaml" or ".yml" or ".json";
            })
            .ToList();

        foreach (var file in files)
        {
            // Parse + validate
            ManifestParseResult parseResult;
            try
            {
                parseResult = _parser.ParseFile(file);
            }
            catch (Exception ex)
            {
                EngineLog.Write($"[PluginLoader] Error reading '{file}': {ex.Message}");
                continue;
            }

            if (!parseResult.Success || parseResult.Manifest is null)
            {
                EngineLog.Write($"[PluginLoader] Skipping invalid manifest '{file}': {string.Join("; ", parseResult.Errors)}");
                continue;
            }

            var manifest = parseResult.Manifest;

            // Check permissions
            _executor.ValidatePermissions(manifest, out var violations);

            // Determine enabled state
            bool enabled;
            if (violations.Count > 0)
            {
                // Force-disable plugins with permission violations regardless of saved state
                enabled = false;
                if (stateMap.ContainsKey(manifest.Id) && stateMap[manifest.Id])
                    EngineLog.Write($"[PluginLoader] Plugin '{manifest.Id}' has permission violations — force-disabled.");
                stateMap[manifest.Id] = false;
            }
            else
            {
                // Default to enabled on first encounter
                enabled = stateMap.TryGetValue(manifest.Id, out var saved) ? saved : true;
                stateMap[manifest.Id] = enabled;
            }

            manifests.Add(new LoadedPlugin(manifest, file, enabled, violations));
        }

        // Persist any state updates (new plugins defaulted to enabled, violating plugins disabled)
        SaveState(stateMap);

        lock (_lock)
            _loaded = manifests;

        EngineLog.Write($"[PluginLoader] Loaded {manifests.Count} plugin(s) from '{_pluginsFolder}'.");
    }

    public IReadOnlyList<IOptimizationHandler> CreateHandlers()
    {
        lock (_lock)
        {
            return _loaded
                .Where(p => p.Enabled && p.PermissionViolations.Count == 0)
                .Select(p => (IOptimizationHandler)new ManifestOptimizationHandler(p.Manifest, _executor))
                .ToList();
        }
    }

    public async Task<bool> InstallFromFileAsync(string sourcePath)
    {
        try
        {
            var parseResult = _parser.ParseFile(sourcePath);
            if (!parseResult.Success || parseResult.Manifest is null)
            {
                EngineLog.Write($"[PluginLoader] InstallFromFile rejected '{sourcePath}': {string.Join("; ", parseResult.Errors)}");
                return false;
            }

            var manifest  = parseResult.Manifest;
            var ext       = Path.GetExtension(sourcePath).ToLowerInvariant();
            var destName  = $"{manifest.Id}{ext}";
            var destPath  = Path.Combine(_pluginsFolder, destName);

            EnsureFolder();
            await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: true));

            Reload();
            EngineLog.Write($"[PluginLoader] Installed plugin '{manifest.Id}' from '{sourcePath}'.");

            _eventBus?.Publish(OptimizerEvent.Create(
                OptimizerEventType.PluginInstalled,
                "Plugin installed",
                manifest.Name,
                new Dictionary<string, string> { ["pluginId"] = manifest.Id }));

            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"[PluginLoader] InstallFromFile error for '{sourcePath}': {ex.Message}");
            return false;
        }
    }

    public bool SetEnabled(string pluginId, bool enabled)
    {
        LoadedPlugin? target;
        lock (_lock)
            target = _loaded.FirstOrDefault(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
            return false;

        // Cannot enable a plugin that has permission violations
        if (enabled && target.PermissionViolations.Count > 0)
        {
            EngineLog.Write($"[PluginLoader] Cannot enable '{pluginId}': permission violations present.");
            return false;
        }

        var state = LoadState();
        state[pluginId] = enabled;
        SaveState(state);

        Reload();
        return true;
    }

    public bool Remove(string pluginId)
    {
        LoadedPlugin? target;
        lock (_lock)
            target = _loaded.FirstOrDefault(p => string.Equals(p.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
            return false;

        try
        {
            if (File.Exists(target.FilePath))
                File.Delete(target.FilePath);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"[PluginLoader] Remove error for '{pluginId}': {ex.Message}");
            return false;
        }

        var state = LoadState();
        state.Remove(pluginId);
        SaveState(state);

        Reload();
        return true;
    }

    // ── State persistence helpers ─────────────────────────────────────────────

    private Dictionary<string, bool> LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
                return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"[PluginLoader] Could not load plugin state: {ex.Message}");
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveState(Dictionary<string, bool> state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFilePath)!);
            var json = JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"[PluginLoader] Could not save plugin state: {ex.Message}");
        }
    }

    private void EnsureFolder() => Directory.CreateDirectory(_pluginsFolder);
}
