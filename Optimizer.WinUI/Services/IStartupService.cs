using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IStartupService
{
    /// <summary>Lists current auto-start entries (enabled, from the Run keys) plus any this app has disabled.</summary>
    List<StartupEntry> GetEntries();

    /// <summary>
    /// Enables or disables a single entry. Disabling moves it to a backup store and removes the Run
    /// value; enabling writes it back. Returns false if the change failed (e.g. HKLM without admin).
    /// </summary>
    bool SetEnabled(StartupEntry entry, bool enabled);
}
