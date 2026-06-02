using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SettingsService : ISettingsService
{
    private static readonly string FilePath = AppPaths.GetDataFile("app-settings.json");

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            Settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
        }
        catch
        {
            Settings = new();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public void Reset()
    {
        Settings = new AppSettings();
        Save();
    }
}
