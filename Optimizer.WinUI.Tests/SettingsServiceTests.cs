using System;
using System.IO;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// SettingsService load/save round-trip against an ISOLATED temp file. These tests previously
/// used the parameterless ctor (the real %LocalAppData% path) — every test run silently wiped
/// the developer's actual settings, including the ApiToken and federation config.
/// </summary>
[Collection("SettingsServiceCollection")]
public class SettingsServiceTests
{
    private static (SettingsService svc, string path) Make()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("optset").FullName, "app-settings.json");
        return (new SettingsService(path), path);
    }

    [Fact]
    public void NewSettingsService_HasDefaultValues()
    {
        var (svc, path) = Make();
        try
        {
            Assert.Equal("Dark", svc.Settings.Theme);
            Assert.Equal("Mica", svc.Settings.BackdropMaterial);
            Assert.Equal(1, svc.Settings.MetricsRefreshSeconds);
            Assert.Equal(60, svc.Settings.ChartHistorySeconds);
            Assert.True(svc.Settings.ConfirmBeforeApply);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Reset_RestoresDefaultValues()
    {
        var (svc, path) = Make();
        try
        {
            svc.Settings.Theme = "Light";
            svc.Settings.MetricsRefreshSeconds = 5;

            svc.Reset();

            Assert.Equal("Dark", svc.Settings.Theme);
            Assert.Equal(1, svc.Settings.MetricsRefreshSeconds);
            Assert.True(File.Exists(path)); // Reset persists
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var (svc, path) = Make();
        try
        {
            svc.Settings.Theme = "Light";
            svc.Settings.MetricsRefreshSeconds = 3;
            svc.Save();

            var svc2 = new SettingsService(path);
            svc2.Load();

            Assert.Equal("Light", svc2.Settings.Theme);
            Assert.Equal(3, svc2.Settings.MetricsRefreshSeconds);
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }

    [Fact]
    public void Malformed_file_loads_defaults_and_keeps_rejected_copy()
    {
        var (_, path) = Make();
        try
        {
            File.WriteAllText(path, "{ this is not json");
            var svc = new SettingsService(path);
            svc.Load();

            Assert.Equal("Dark", svc.Settings.Theme); // defaults in memory
            Assert.True(File.Exists(path + ".rejected")); // forensics copy
            Assert.Equal("{ this is not json", File.ReadAllText(path + ".rejected"));
        }
        finally { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); }
    }
}
