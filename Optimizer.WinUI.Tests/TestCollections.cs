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
