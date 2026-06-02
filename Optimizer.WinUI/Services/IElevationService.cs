namespace Optimizer.WinUI.Services;

/// <summary>
/// Detects and requests administrator elevation. Several system-wide tweaks
/// (HKLM registry, power schemes) require an elevated process to take effect.
/// </summary>
public interface IElevationService
{
    /// <summary>True if the current process is running with administrator rights.</summary>
    bool IsElevated { get; }

    /// <summary>
    /// Attempts to relaunch the current executable elevated (UAC prompt).
    /// Returns true if a new elevated process was started (the caller should then exit).
    /// </summary>
    bool TryRelaunchElevated();
}
