using System;
using System.IO;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FancontrolStatusServiceTests
{
    // Verbatim copies of the real Fancontrol state contracts (2026-06-12).
    private const string BrainJson = """
    {"night":false,"cpu":{"l":7.9,"t":48,"w":77.9},"fcWatchdogOff":false,"hint":null,"pumpRpm":1732,"nightOverride":null,"demands":{"case":40,"rad":38},"load":false,"gpu":{"l":12,"t":39,"w":69.8,"mem":50},"ts":"2026-06-12T14:19:28.9673917+02:00","apps":["destiny2"],"cool":35.2,"trim":0,"game":false,"lhmOk":true,"app":"destiny2","alarm":false,"mode":"APP[destiny2]"}
    """;

    private const string FgwatchJson = """
    {"auto":null,"veto":["destiny2"],"autoExe":null,"lastApplied":"AAA-HDR","applyTime":null,"mapped":2,"cand":null,"fg":"claude","candCnt":0,"pollSec":3,"enabled":true,"ts":"2026-06-12T14:19:29.9648090+02:00","prev":null}
    """;

    private const string SentinelJson = """
    {"ts":"2026-06-12T13:50:08.1090485+02:00","pass":true,"radMin":26,"pump":[50],"radMax":26,"coolant":{"avg":36,"max":36.1},"issues":["radiator >=40% over half the window"]}
    """;

    [Fact]
    public void ParseBrain_maps_the_real_contract()
    {
        var b = FancontrolStatusService.ParseBrain(BrainJson);

        Assert.Equal("APP[destiny2]", b.Mode);
        Assert.False(b.Game);
        Assert.False(b.Night);
        Assert.False(b.Alarm);
        Assert.True(b.LhmOk);
        Assert.Equal(48, b.CpuTemp);
        Assert.Equal(77.9, b.CpuWatts);
        Assert.Equal(7.9, b.CpuLoad);
        Assert.Equal(39, b.GpuTemp);
        Assert.Equal(69.8, b.GpuWatts);
        Assert.Equal(35.2, b.Coolant);
        Assert.Equal(1732, b.PumpRpm);
        Assert.Equal(40, b.CaseDemand);
        Assert.Equal(38, b.RadDemand);
        Assert.Equal("destiny2", b.ActiveApp);
        Assert.Equal(["destiny2"], b.RunningApps);
        Assert.Equal(new DateTimeOffset(2026, 6, 12, 14, 19, 28, TimeSpan.FromHours(2)).Date, b.Timestamp.Date);
    }

    [Fact]
    public void ParseProfiles_maps_the_real_contract()
    {
        var p = FancontrolStatusService.ParseProfiles(FgwatchJson);

        Assert.Equal("AAA-HDR", p.LastAppliedProfile);
        Assert.True(p.Enabled);
        Assert.Equal(2, p.MappedPrograms);
        Assert.Equal("claude", p.ForegroundExe);
        Assert.Equal(["destiny2"], p.VetoApps);
    }

    [Fact]
    public void ParseSentinel_maps_the_real_contract()
    {
        var s = FancontrolStatusService.ParseSentinel(SentinelJson);

        Assert.True(s.Pass);
        Assert.Equal(36, s.CoolantAvg);
        Assert.Equal(36.1, s.CoolantMax);
        Assert.Single(s.Issues);
    }

    [Fact]
    public void Unconfigured_service_returns_null_and_IsConfigured_false()
    {
        var svc = new FancontrolStatusService("");
        Assert.False(svc.IsConfigured);
        Assert.Null(svc.GetStatus());
    }

    [Fact]
    public void Reads_state_dir_marks_old_timestamps_stale_and_missing_sections_null()
    {
        var dir = Directory.CreateTempSubdirectory("fcstate").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "brain_state.json"), BrainJson); // ts is 2026-06-12 → stale by now
            // no fgwatch/sentinel files

            var svc = new FancontrolStatusService(dir);
            var status = svc.GetStatus();

            Assert.NotNull(status);
            Assert.NotNull(status!.Brain);
            Assert.True(status.Brain!.Stale);
            Assert.Equal("APP[destiny2]", status.Brain.Mode);
            Assert.Null(status.Profiles);
            Assert.Null(status.Sentinel);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Truncated_midwrite_file_serves_last_good_value()
    {
        var dir = Directory.CreateTempSubdirectory("fcstate").FullName;
        try
        {
            var brainPath = Path.Combine(dir, "brain_state.json");
            File.WriteAllText(brainPath, BrainJson);
            var svc = new FancontrolStatusService(dir);
            Assert.Equal(35.2, svc.GetStatus()!.Brain!.Coolant);

            // The brain replaces this file every 5 s — simulate catching it mid-write.
            File.WriteAllText(brainPath, BrainJson.Substring(0, 50));
            var status = svc.GetStatus();

            Assert.NotNull(status!.Brain);
            Assert.Equal(35.2, status.Brain!.Coolant); // last good, not an exception or null
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
