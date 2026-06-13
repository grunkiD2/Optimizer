using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Serialises ProfileService test classes to prevent file-lock contention on snapshots.json.
/// Both ProfileServiceTests and ProfileServiceExtendedTests write to the same file.
/// </summary>
[CollectionDefinition("ProfileServiceCollection", DisableParallelization = true)]
public class ProfileServiceCollection { }

/// <summary>
/// Serialises SettingsService test classes to prevent file-lock contention on app-settings.json.
/// SettingsServiceTests and SettingsMergeTests both write to the same file.
/// </summary>
[CollectionDefinition("SettingsServiceCollection", DisableParallelization = true)]
public class SettingsServiceCollection { }

/// <summary>
/// Serialises registry-touching test classes that share the sacrificial key
/// HKCU\Software\OptimizerPluginTest. Without this, parallel class execution races:
/// one class's Dispose (DeleteSubKeyTree) wipes the key while another is mid-test,
/// so the undo capture sees no prior value and undo deletes instead of restoring.
/// </summary>
[CollectionDefinition("RegistryTests", DisableParallelization = true)]
public class RegistryTestsCollection { }

/// <summary>
/// Serialises test classes whose assertions depend on the global static <c>EngineLog.LineWritten</c>
/// bus (e.g. ConsoleViewModel subscribes to it; the assistant console test captures it). xUnit runs
/// classes in parallel, and many unrelated tests emit EngineLog lines — those fire on foreign
/// threads into the same static event, racing the (non-thread-safe) ObservableCollection/List these
/// tests inspect and dropping the test's own line. DisableParallelization runs them in isolation so
/// no foreign EngineLog emission overlaps. (Observed flake 2026-06-13.)
/// </summary>
[CollectionDefinition("EngineLog", DisableParallelization = true)]
public class EngineLogCollection { }
