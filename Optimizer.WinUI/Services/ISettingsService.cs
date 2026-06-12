using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Load();
    void Save();
    void Reset();

    /// <summary>Raised after Save() completes, so listeners can react to setting changes.</summary>
    event Action? SettingsChanged;
}
