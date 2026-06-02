using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Basic smoke tests for SettingsService load/save round-trip.
/// NOTE: SettingsService hard-codes its file path to %LocalAppData%\Optimizer\app-settings.json,
/// so these tests operate against the real path. A future refactor to inject the path would
/// allow full isolation. For now the tests verify the public API contract.
/// </summary>
public class SettingsServiceTests
{
    [Fact]
    public void NewSettingsService_HasDefaultValues()
    {
        var svc = new SettingsService();

        Assert.Equal("Dark", svc.Settings.Theme);
        Assert.Equal("Mica", svc.Settings.BackdropMaterial);
        Assert.Equal(1, svc.Settings.MetricsRefreshSeconds);
        Assert.Equal(60, svc.Settings.ChartHistorySeconds);
        Assert.True(svc.Settings.ConfirmBeforeApply);
    }

    [Fact]
    public void Reset_RestoresDefaultValues()
    {
        var svc = new SettingsService();

        // Mutate
        svc.Settings.Theme = "Light";
        svc.Settings.MetricsRefreshSeconds = 5;

        // Reset should restore defaults (and write to disk)
        svc.Reset();

        Assert.Equal("Dark", svc.Settings.Theme);
        Assert.Equal(1, svc.Settings.MetricsRefreshSeconds);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var svc = new SettingsService();

        // Capture current state so we can restore it
        var originalTheme = svc.Settings.Theme;
        var originalRefresh = svc.Settings.MetricsRefreshSeconds;

        try
        {
            svc.Settings.Theme = "Light";
            svc.Settings.MetricsRefreshSeconds = 3;
            svc.Save();

            // A fresh instance loading the same file should see the saved values
            var svc2 = new SettingsService();
            svc2.Load();

            Assert.Equal("Light", svc2.Settings.Theme);
            Assert.Equal(3, svc2.Settings.MetricsRefreshSeconds);
        }
        finally
        {
            // Restore original values so the test doesn't pollute the dev environment
            svc.Settings.Theme = originalTheme;
            svc.Settings.MetricsRefreshSeconds = originalRefresh;
            svc.Save();
        }
    }
}
