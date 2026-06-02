using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISettingsService
{
    AppSettings Settings { get; }
    void Load();
    void Save();
    void Reset();
}
