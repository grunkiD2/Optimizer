using System.IO;
using System.Text.Json;

namespace Optimizer.Services
{
    /// <summary>User preferences, persisted to %LocalAppData%\Optimizer\settings.json.</summary>
    public class AppSettings
    {
        public int MetricsRefreshSeconds { get; set; } = 2;
        public string Theme { get; set; } = "FluentLight";
        public bool StartWithWindows { get; set; }
        public bool ConfirmBeforeApply { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public int LastTabIndex { get; set; }
        public double WindowWidth { get; set; } = 1200;
        public double WindowHeight { get; set; } = 800;
        public bool HasSeenOnboarding { get; set; }
        public bool AlwaysRunAsAdmin { get; set; } = true;
        public bool AutoSwitchEnabled { get; set; }
        public string OnAcProfileId { get; set; } = string.Empty;
        public string OnBatteryProfileId { get; set; } = string.Empty;
        public string UpdateFeedUrl { get; set; } = string.Empty;
    }

    public interface ISettingsService
    {
        AppSettings Settings { get; }
        void Save();
    }

    public class SettingsService : ISettingsService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Optimizer", "settings.json");

        public SettingsService()
        {
            Settings = Load();
        }

        public AppSettings Settings { get; private set; }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load settings; using defaults.");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to save settings.");
            }
        }
    }
}
