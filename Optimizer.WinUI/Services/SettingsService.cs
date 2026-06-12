using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SettingsService : ISettingsService
{
    private readonly string _filePath;

    public SettingsService() : this(AppPaths.GetDataFile("app-settings.json")) { }

    /// <summary>Test seam: point the service at a temp file. Tests using the default ctor
    /// wrote the REAL %LocalAppData% file and silently wiped user settings (incl. ApiToken).</summary>
    public SettingsService(string filePath) => _filePath = filePath;

    public AppSettings Settings { get; private set; } = new();

    /// <inheritdoc />
    public event Action? SettingsChanged;

    public void Load()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch (Exception ex)
        {
            // A malformed file must not silently become defaults on disk (that regenerates
            // ApiToken and drops machine config): keep a .rejected copy for forensics.
            try { File.Copy(_filePath, _filePath + ".rejected", overwrite: true); } catch { }
            EngineLog.Error($"app-settings.json failed to parse — using defaults (rejected copy kept at {_filePath}.rejected)", ex);
            Settings = new();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
        SettingsChanged?.Invoke();
    }

    public void Reset()
    {
        Settings = new AppSettings();
        Save();
    }
}
