using System.IO;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for SettingsService.ApplyRemoteSettings — verifies synced fields are updated
/// while per-device fields are preserved. Uses ISOLATED temp files (the parameterless
/// ctor writes the developer's real %LocalAppData% settings — never use it in tests).
/// </summary>
[Collection("SettingsServiceCollection")]
public class SettingsMergeTests
{
    private static SettingsService MakeIsolated() =>
        new(Path.Combine(Directory.CreateTempSubdirectory("optmerge").FullName, "app-settings.json"));

    [Fact]
    public void ApplyRemoteSettings_UpdatesSyncedFields()
    {
        var svc = MakeIsolated();
        var originalTheme = svc.Settings.Theme;
        var originalRefresh = svc.Settings.MetricsRefreshSeconds;

        try
        {
            svc.Settings.Theme = "Dark";
            svc.Settings.MetricsRefreshSeconds = 1;
            svc.Save();

            var remote = new AppSettings
            {
                Theme = "Light",
                MetricsRefreshSeconds = 5,
                ChartHistorySeconds = 120,
                BackdropMaterial = "Acrylic",
                AccentColor = "#FF0000",
                ConfirmBeforeApply = false,
                MinimizeToTray = false,
                StartMinimized = true,
                StartWithWindows = true,
                NotifyPerformance = false,
                NotifyStorage = false,
                NotifyHardware = false,
                NotifySecurity = false,
                NotifyRecommendations = false,
                NotifyOptimizations = true,
                UsageProfile = "Gaming",
                Language = "fr-FR"
            };

            svc.ApplyRemoteSettings(remote);

            Assert.Equal("Light", svc.Settings.Theme);
            Assert.Equal(5, svc.Settings.MetricsRefreshSeconds);
            Assert.Equal(120, svc.Settings.ChartHistorySeconds);
            Assert.Equal("Acrylic", svc.Settings.BackdropMaterial);
            Assert.Equal("#FF0000", svc.Settings.AccentColor);
            Assert.False(svc.Settings.ConfirmBeforeApply);
            Assert.False(svc.Settings.MinimizeToTray);
            Assert.True(svc.Settings.StartMinimized);
            Assert.True(svc.Settings.StartWithWindows);
            Assert.False(svc.Settings.NotifyPerformance);
            Assert.True(svc.Settings.NotifyOptimizations);
            Assert.Equal("Gaming", svc.Settings.UsageProfile);
            Assert.Equal("fr-FR", svc.Settings.Language);
        }
        finally
        {
            svc.Settings.Theme = originalTheme;
            svc.Settings.MetricsRefreshSeconds = originalRefresh;
            svc.Save();
        }
    }

    [Fact]
    public void ApplyRemoteSettings_PreservesPerDeviceFields()
    {
        var svc = MakeIsolated();

        // Capture current per-device values to restore later
        var originalNav = svc.Settings.LastNavigationItem;
        var originalWidth = svc.Settings.WindowWidth;
        var originalHeight = svc.Settings.WindowHeight;
        var originalApiEnabled = svc.Settings.ApiEnabled;
        var originalApiPort = svc.Settings.ApiPort;
        var originalApiToken = svc.Settings.ApiToken;
        var originalCloudEnabled = svc.Settings.CloudSyncEnabled;
        var originalCloudUrl = svc.Settings.CloudServerUrl;
        var originalOnboarding = svc.Settings.HasCompletedOnboarding;
        var originalTheme = svc.Settings.Theme;

        try
        {
            // Set local per-device values
            svc.Settings.LastNavigationItem = "Performance";
            svc.Settings.WindowWidth = 1920;
            svc.Settings.WindowHeight = 1080;
            svc.Settings.ApiEnabled = true;
            svc.Settings.ApiPort = 9999;
            svc.Settings.ApiToken = "local-token-xyz";
            svc.Settings.CloudSyncEnabled = true;
            svc.Settings.CloudServerUrl = "http://local:5000";
            svc.Settings.HasCompletedOnboarding = true;
            svc.Save();

            // Remote has different per-device values — they should NOT be applied
            var remote = new AppSettings
            {
                LastNavigationItem = "Network",
                WindowWidth = 1280,
                WindowHeight = 720,
                ApiEnabled = false,
                ApiPort = 1234,
                ApiToken = "remote-token-abc",
                CloudSyncEnabled = false,
                CloudServerUrl = "http://remote:5000",
                HasCompletedOnboarding = false,
                Theme = "Light"  // This synced field should be applied
            };

            svc.ApplyRemoteSettings(remote);

            // Per-device fields unchanged
            Assert.Equal("Performance", svc.Settings.LastNavigationItem);
            Assert.Equal(1920, svc.Settings.WindowWidth);
            Assert.Equal(1080, svc.Settings.WindowHeight);
            Assert.True(svc.Settings.ApiEnabled);
            Assert.Equal(9999, svc.Settings.ApiPort);
            Assert.Equal("local-token-xyz", svc.Settings.ApiToken);
            Assert.True(svc.Settings.CloudSyncEnabled);
            Assert.Equal("http://local:5000", svc.Settings.CloudServerUrl);
            Assert.True(svc.Settings.HasCompletedOnboarding);

            // Synced field was applied
            Assert.Equal("Light", svc.Settings.Theme);
        }
        finally
        {
            svc.Settings.LastNavigationItem = originalNav;
            svc.Settings.WindowWidth = originalWidth;
            svc.Settings.WindowHeight = originalHeight;
            svc.Settings.ApiEnabled = originalApiEnabled;
            svc.Settings.ApiPort = originalApiPort;
            svc.Settings.ApiToken = originalApiToken;
            svc.Settings.CloudSyncEnabled = originalCloudEnabled;
            svc.Settings.CloudServerUrl = originalCloudUrl;
            svc.Settings.HasCompletedOnboarding = originalOnboarding;
            svc.Settings.Theme = originalTheme;
            svc.Save();
        }
    }

    [Fact]
    public void ApplyRemoteSettings_TriggersSettingsChangedEvent()
    {
        var svc = MakeIsolated();
        var originalTheme = svc.Settings.Theme;

        try
        {
            var fired = false;
            svc.SettingsChanged += () => fired = true;

            svc.ApplyRemoteSettings(new AppSettings { Theme = "Light" });

            Assert.True(fired);
        }
        finally
        {
            svc.Settings.Theme = originalTheme;
            svc.Save();
        }
    }

    [Fact]
    public void Save_TriggersSettingsChangedEvent()
    {
        var svc = MakeIsolated();
        var firedCount = 0;
        svc.SettingsChanged += () => firedCount++;

        svc.Save();

        Assert.Equal(1, firedCount);
    }
}
