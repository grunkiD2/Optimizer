using System;
using System.IO;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// R2: contract tests bound to PRODUCER-generated fixtures (tools\Update-FancontrolFixtures.ps1
/// harvests the live machine's state files + real ctl.ps1 output). Hand-copied strings only ever
/// bind the consumer — these fixtures make an engine-side field rename (e.g. cool→coolant) turn
/// the suite red after a fixture refresh instead of staying silently green. Fixtures are captured
/// health-gated (sentinel pass, live brain, coolant present), so the asserts below are strict.
/// </summary>
public class FancontrolContractFixtureTests
{
    private static string Fx(string name)
        => Path.Combine(AppContext.BaseDirectory, "Fixtures", "fancontrol", name);

    [Fact]
    public void Brain_fixture_carries_every_core_field()
    {
        var s = FancontrolStatusService.ParseBrain(File.ReadAllText(Fx("brain_state.json")));
        Assert.Equal(1, s.SchemaVersion);
        Assert.NotEqual(DateTimeOffset.MinValue, s.Timestamp);
        Assert.False(string.IsNullOrEmpty(s.Mode));
        Assert.True(s.LhmOk);
        Assert.NotNull(s.CpuTemp);
        Assert.NotNull(s.CpuWatts);
        Assert.NotNull(s.GpuTemp);
        Assert.NotNull(s.GpuWatts);
        Assert.NotNull(s.CaseDemand);
        Assert.NotNull(s.RadDemand);
        Assert.NotNull(s.Coolant);   // capture is gated on a live Corsair tap
        Assert.NotNull(s.PumpRpm);
    }

    [Fact]
    public void Fgwatch_fixture_carries_every_core_field()
    {
        var s = FancontrolStatusService.ParseProfiles(File.ReadAllText(Fx("fgwatch_state.json")));
        Assert.Equal(1, s.SchemaVersion);
        Assert.NotEqual(DateTimeOffset.MinValue, s.Timestamp);
        Assert.True(s.Enabled);                  // capture is gated on a running daemon
        Assert.True(s.MappedPrograms >= 1);      // this machine maps at least one program
    }

    [Fact]
    public void Sentinel_fixture_carries_every_core_field()
    {
        var s = FancontrolStatusService.ParseSentinel(File.ReadAllText(Fx("sentinel_state.json")));
        Assert.Equal(1, s.SchemaVersion);
        Assert.NotEqual(DateTimeOffset.MinValue, s.Timestamp);
        Assert.True(s.Pass);                     // capture is health-gated, so pass MUST parse true
        Assert.Empty(s.Issues);
    }

    [Fact]
    public void Telemetry_fixture_line_parses_with_core_columns()
    {
        var row = FancontrolTelemetryService.ParseTelemetryLine(File.ReadAllText(Fx("telemetry_line.jsonl")));
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty((string)row!["Ts"]));
        Assert.False(string.IsNullOrEmpty((string)row["Mode"]));
        Assert.IsType<double>(row["CpuTemp"]);   // DBNull here would mean the cpu.t path broke
        Assert.IsType<double>(row["GpuTemp"]);
        Assert.IsType<double>(row["CpuWatts"]);
    }

    [Fact]
    public void Ctl_ok_fixture_parses_as_success()
    {
        var r = FancontrolCommandService.ParseCtlResult(File.ReadAllText(Fx("ctl_result_ok.json")), "", 0);
        Assert.True(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Output));
    }

    [Fact]
    public void Ctl_fail_fixture_parses_as_failure()
    {
        var r = FancontrolCommandService.ParseCtlResult(File.ReadAllText(Fx("ctl_result_fail.json")), "", 1);
        Assert.False(r.Success);
        Assert.False(string.IsNullOrWhiteSpace(r.Output));
    }
}
