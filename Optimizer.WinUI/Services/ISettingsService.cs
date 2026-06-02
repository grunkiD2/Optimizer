using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Load();
    void Save();
    void Reset();

    /// <summary>
    /// Raised after Save() completes. Used by CloudSyncOrchestrator to detect setting changes
    /// and trigger a debounced push.
    /// </summary>
    event Action? SettingsChanged;

    /// <summary>
    /// Merges remote settings into local Settings, preserving per-device fields.
    /// Calls Save() when complete.
    /// </summary>
    void ApplyRemoteSettings(AppSettings remote);
}
