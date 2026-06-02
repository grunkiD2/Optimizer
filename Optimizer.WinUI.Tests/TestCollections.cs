using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Serialises ProfileService test classes to prevent file-lock contention on snapshots.json.
/// Both ProfileServiceTests and ProfileServiceExtendedTests write to the same file.
/// </summary>
[CollectionDefinition("ProfileServiceCollection", DisableParallelization = true)]
public class ProfileServiceCollection { }
