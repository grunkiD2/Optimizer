using Optimizer.WinUI.Models.Plugins;
using Optimizer.WinUI.Services.Optimizations;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Represents a plugin manifest that was discovered in the plugins folder,
/// along with its loaded state and any permission or parse violations.
/// </summary>
/// <param name="Manifest">The parsed manifest (never null here; failed parses are discarded).</param>
/// <param name="FilePath">Absolute path to the manifest file on disk.</param>
/// <param name="Enabled">Whether the user has enabled this plugin.</param>
/// <param name="PermissionViolations">Non-empty when the manifest contains disallowed paths; plugin is force-disabled.</param>
public record LoadedPlugin(
    OptimizationManifest Manifest,
    string FilePath,
    bool Enabled,
    IReadOnlyList<string> PermissionViolations);

/// <summary>
/// Discovers, validates, and manages community plugin manifests stored in the per-user plugins folder.
/// Provides <see cref="CreateHandlers"/> to obtain live <see cref="IOptimizationHandler"/> instances
/// that integrate seamlessly with the built-in optimization pipeline.
/// </summary>
public interface IPluginLoader
{
    /// <summary>Absolute path of the folder that is scanned for plugin manifests.</summary>
    string PluginsFolder { get; }

    /// <summary>
    /// The most recently loaded set of plugins (populated after construction and after each <see cref="Reload"/>).
    /// Includes both enabled and disabled plugins, and those with permission violations.
    /// </summary>
    IReadOnlyList<LoadedPlugin> LoadedPlugins { get; }

    /// <summary>Re-scans <see cref="PluginsFolder"/>, re-parses manifests, and refreshes <see cref="LoadedPlugins"/>.</summary>
    void Reload();

    /// <summary>
    /// Returns one <see cref="IOptimizationHandler"/> for every plugin that is
    /// <em>enabled</em> and has <em>no permission violations</em>.
    /// </summary>
    IReadOnlyList<IOptimizationHandler> CreateHandlers();

    /// <summary>
    /// Parses and validates <paramref name="sourcePath"/>;
    /// if valid, copies it into <see cref="PluginsFolder"/> as <c>{id}.yaml</c> (or preserving the source extension),
    /// then calls <see cref="Reload"/>.
    /// </summary>
    /// <returns>True on success; false if the file is invalid or a filesystem error occurs.</returns>
    Task<bool> InstallFromFileAsync(string sourcePath);

    /// <summary>
    /// Enables or disables a plugin by ID. Persists the state and reloads.
    /// Returns false if no plugin with <paramref name="pluginId"/> is loaded.
    /// </summary>
    bool SetEnabled(string pluginId, bool enabled);

    /// <summary>
    /// Permanently removes a plugin: deletes the manifest file, updates persisted state, and reloads.
    /// Returns false if no plugin with <paramref name="pluginId"/> is found.
    /// </summary>
    bool Remove(string pluginId);
}
