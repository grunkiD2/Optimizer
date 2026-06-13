// ProfileIntelligenceServiceTests.cs
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Moq;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ProfileIntelligenceServiceTests
{
    // Shapes verified live against FancontrolCommandService.cs (Rule #1, 2026-06-14):
    //   FancontrolProfile(Name, Dc, Bright, Hdr, Power, Lyd, LysMode, LysColor, Optimizer,
    //                     UiIcon, UiDesc, GamingClass, HdrType="")  — 13 params (HdrType defaults).
    //   FancontrolProgramInfo(Exe, Name, Profile, CaseFloor?, RadFloor?,
    //                         LearnedGpuP95?(double), LearnedGpuWatts?(double), LearnedSamples?(int)).
    private static Mock<IFancontrolCommandService> FcWith(params FancontrolProgramInfo[] progs)
    {
        var m = new Mock<IFancontrolCommandService>();
        m.Setup(x => x.GetMappedPrograms()).Returns(progs);
        m.Setup(x => x.GetProfiles()).Returns(new[]
        {
            new FancontrolProfile("AAA-HDR", 8, 80, true, "", "", "synapse", null, "", "", "", GamingClass: true),
        });
        return m;
    }

    [Fact]
    public void Build_includes_measured_learned_stats_for_mapped_app()
    {
        var root = Directory.CreateTempSubdirectory("intel").FullName;
        try
        {
            var fc = FcWith(new FancontrolProgramInfo("destiny2.exe", "Destiny 2", "AAA-HDR", 48, 44, 59.3, 211, 749));
            var svc = new ProfileIntelligenceService(fc.Object, new PresentMonSummaryReader(root), webLookup: null);

            var pic = svc.Build(profileName: "AAA-HDR", foregroundExe: "destiny2.exe");

            Assert.Equal("destiny2.exe", pic.AppName);
            var measured = pic.Groups.SelectMany(g => g.Lines).Where(l => l.Tier == ConfidenceTier.Measured).ToList();
            Assert.Contains(measured, l => l.Label.Contains("GPU") && l.Value.Contains("59"));
            Assert.Contains(measured, l => l.Label.Contains("watt") || l.Value.Contains("211"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Build_marks_empty_measurement_when_no_sessions_yet()
    {
        var root = Directory.CreateTempSubdirectory("intel").FullName;
        try
        {
            var fc = FcWith(new FancontrolProgramInfo("fresh.exe", "Fresh", "AAA-HDR", 48, 44, null, null, null));
            var svc = new ProfileIntelligenceService(fc.Object, new PresentMonSummaryReader(root), webLookup: null);

            var pic = svc.Build("AAA-HDR", "fresh.exe");

            Assert.True(pic.MaturityHave < pic.MaturityTarget);
            // tomt målefelt vises eksplicit (spec §3) — ikke skjult
            Assert.Contains(pic.Groups.SelectMany(g => g.Lines), l => l.Value.Contains("endnu ingen", System.StringComparison.OrdinalIgnoreCase));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Build_identity_group_marks_gamingClass_authority_as_derived_not_measured()
    {
        var root = Directory.CreateTempSubdirectory("intel").FullName;
        try
        {
            var fc = FcWith(new FancontrolProgramInfo("destiny2.exe", "Destiny 2", "AAA-HDR", 48, 44, 59.3, 211, 749));
            var svc = new ProfileIntelligenceService(fc.Object, new PresentMonSummaryReader(root), webLookup: null);
            var pic = svc.Build("AAA-HDR", "destiny2.exe");
            var id = pic.Groups.First(g => g.Title.Contains("Identitet"));
            Assert.Contains(id.Lines, l => l.Label.Contains("klassifikation") && l.Tier == ConfidenceTier.Derived);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
