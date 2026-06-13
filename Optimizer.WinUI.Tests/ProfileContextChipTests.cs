// ProfileContextChipTests.cs
using System;
using System.Collections.Generic;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ProfileContextChipTests
{
    // Bygger en minimal FancontrolStatus. Felt-navnene matcher Models\FancontrolStatus.cs verificeret
    // (Brain {Mode, ActiveApp, Stale}, Profiles {Enabled, LastAppliedProfile, ForegroundExe, Stale}).
    private static FancontrolStatus Status(string mode, bool enabled, string? lastProfile, string? fgExe, bool stale = false)
        => new()
        {
            Brain = new FancontrolBrainStatus { Mode = mode, ActiveApp = fgExe, Stale = stale },
            Profiles = new FancontrolProfileStatus { Enabled = enabled, LastAppliedProfile = lastProfile, ForegroundExe = fgExe, Stale = stale },
            Sentinel = new FancontrolSentinelStatus { Pass = true },
        };

    [Fact]
    public void AppBound_when_fgwatch_enabled_and_foreground_is_mapped()
    {
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "destiny2.exe" };
        var chip = ProfileContextChip.Derive(Status("AAA-HDR", enabled: true, "AAA-HDR", "destiny2.exe"), mapped);
        Assert.Equal(ChipKind.AppBound, chip.Kind);
        Assert.Equal("destiny2.exe", chip.AppExe);
        Assert.Equal("AAA-HDR", chip.ProfileName);
    }

    [Fact]
    public void Mood_when_foreground_is_not_a_mapped_program()
    {
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "destiny2.exe" };
        var chip = ProfileContextChip.Derive(Status("Desktop", enabled: true, "Desktop", "explorer.exe"), mapped);
        Assert.Equal(ChipKind.Mood, chip.Kind);
        Assert.Equal("Desktop", chip.ProfileName);
    }

    [Fact]
    public void Mood_when_fgwatch_disabled_even_if_foreground_mapped()
    {
        var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "destiny2.exe" };
        var chip = ProfileContextChip.Derive(Status("AAA-HDR", enabled: false, "AAA-HDR", "destiny2.exe"), mapped);
        Assert.Equal(ChipKind.Mood, chip.Kind);
    }

    [Fact]
    public void Stale_flag_propagates()
    {
        var chip = ProfileContextChip.Derive(Status("Desktop", true, "Desktop", null, stale: true), new HashSet<string>());
        Assert.True(chip.Stale);
    }

    [Fact]
    public void Null_status_is_unknown_not_a_crash()
    {
        var chip = ProfileContextChip.Derive(null, new HashSet<string>());
        Assert.Equal(ChipKind.Unknown, chip.Kind);
    }
}
