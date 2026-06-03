using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Models.Gpu;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Gpu;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for the MANAGED safety logic in GpuControlService:
/// backend selection, clamping, apply routing, watchdog, telemetry mapping.
/// No real NVAPI or ADL calls are made — all hardware interaction is faked.
/// </summary>
public class GpuControlServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Mock<IGpuControlBackend> MakeBackend(
        GpuVendor vendor,
        bool isAvailable,
        string? unavailableReason = null,
        bool applyOk = true,
        string applyError = "")
    {
        var m = new Mock<IGpuControlBackend>();
        m.Setup(b => b.Vendor).Returns(vendor);
        m.Setup(b => b.IsAvailable).Returns(isAvailable);
        m.Setup(b => b.UnavailableReason).Returns(unavailableReason);
        m.Setup(b => b.GetCapabilities()).Returns(new GpuControlCapabilities
        {
            CanReadTelemetry   = isAvailable,
            CanSetCoreOffset   = isAvailable,
            CanSetMemoryOffset = isAvailable,
            CanSetPowerLimit   = isAvailable,
            CanSetTempLimit    = isAvailable,
            CanSetFan          = isAvailable,
            CoreOffsetRangeMhz        = (-200, 300),
            MemoryOffsetRangeMhz      = (-500, 1500),
            PowerLimitRangePercent    = (50, 120),
        });
        m.Setup(b => b.TryApply(It.IsAny<GpuControlState>(), out applyError))
         .Returns(applyOk);
        return m;
    }

    private static Mock<ISensorService> MakeSensors(double? gpuTempC = null, bool isAvailable = true)
    {
        var snap = new HardwareSnapshot();
        if (gpuTempC.HasValue)
        {
            snap.GpuTemperatures.Add(new SensorReading
            {
                Name         = "GPU Core",
                HardwareName = "NVIDIA GeForce Test",
                Value        = gpuTempC.Value,
                Kind         = SensorKind.Temperature,
                Unit         = "°C",
            });
        }
        snap.GpuClocks.Add(new SensorReading
        {
            Name         = "GPU Core",
            HardwareName = "NVIDIA GeForce Test",
            Value        = 1800,
            Kind         = SensorKind.Clock,
            Unit         = "MHz",
        });
        snap.GpuClocks.Add(new SensorReading
        {
            Name         = "GPU Memory",
            HardwareName = "NVIDIA GeForce Test",
            Value        = 7000,
            Kind         = SensorKind.Clock,
            Unit         = "MHz",
        });
        snap.GpuLoads.Add(new SensorReading
        {
            Name         = "GPU Core",
            HardwareName = "NVIDIA GeForce Test",
            Value        = 75,
            Kind         = SensorKind.Load,
            Unit         = "%",
        });
        snap.GpuPowers.Add(new SensorReading
        {
            Name         = "GPU Power",
            HardwareName = "NVIDIA GeForce Test",
            Value        = 200,
            Kind         = SensorKind.Power,
            Unit         = "W",
        });

        var m = new Mock<ISensorService>();
        m.Setup(s => s.IsAvailable).Returns(isAvailable);
        m.Setup(s => s.GetSnapshot()).Returns(snap);
        return m;
    }

    // ── Backend selection ─────────────────────────────────────────────────────

    [Fact]
    public void BackendSelection_PicksFirstAvailableBackend()
    {
        var nvBackend   = MakeBackend(GpuVendor.Nvidia, isAvailable: true);
        var adlBackend  = MakeBackend(GpuVendor.Amd,   isAvailable: false, "AMD not found");
        var nullBackend = new NullGpuBackend();
        var sensors     = MakeSensors();

        var svc = new GpuControlService(sensors.Object,
            new IGpuControlBackend[] { nvBackend.Object, adlBackend.Object, nullBackend });

        Assert.True(svc.OcWriteAvailable);
        Assert.Equal(GpuVendor.Nvidia, svc.PrimaryVendor);
    }

    [Fact]
    public void BackendSelection_FallsBackToNullWhenNoneAvailable()
    {
        var nvBackend  = MakeBackend(GpuVendor.Nvidia, isAvailable: false, "NVAPI not found");
        var adlBackend = MakeBackend(GpuVendor.Amd,   isAvailable: false, "ADL not found");
        var sensors    = MakeSensors();

        var svc = new GpuControlService(sensors.Object,
            new IGpuControlBackend[] { nvBackend.Object, adlBackend.Object });

        Assert.False(svc.OcWriteAvailable);
        Assert.NotNull(svc.OcUnavailableReason);
    }

    [Fact]
    public void NullBackend_IsAvailableFalse_TryApplyReturnsFalse()
    {
        var backend = new NullGpuBackend();
        Assert.False(backend.IsAvailable);
        Assert.NotNull(backend.UnavailableReason);
        Assert.False(backend.TryApply(new GpuControlState(), out var err));
        Assert.NotEmpty(err);
    }

    // ── Clamping ──────────────────────────────────────────────────────────────

    private static GpuControlCapabilities DefaultCaps() => new()
    {
        CoreOffsetRangeMhz        = (-200, 300),
        MemoryOffsetRangeMhz      = (-500, 1500),
        PowerLimitRangePercent    = (50, 120),
    };

    [Fact]
    public void Clamp_CoreOffsetAboveMax_IsClamped()
    {
        var caps    = DefaultCaps();
        var desired = new GpuControlState { CoreClockOffsetMhz = 999 };
        var clamped = GpuControlService.Clamp(desired, caps);
        Assert.Equal(300, clamped.CoreClockOffsetMhz);
    }

    [Fact]
    public void Clamp_CoreOffsetBelowMin_IsClamped()
    {
        var caps    = DefaultCaps();
        var desired = new GpuControlState { CoreClockOffsetMhz = -999 };
        var clamped = GpuControlService.Clamp(desired, caps);
        Assert.Equal(-200, clamped.CoreClockOffsetMhz);
    }

    [Fact]
    public void Clamp_PowerLimitOutOfRange_IsClamped()
    {
        var caps = DefaultCaps();

        var tooHigh = new GpuControlState { PowerLimitPercent = 200, CoreClockOffsetMhz = 0, TempLimitC = 80 };
        Assert.Equal(120, GpuControlService.Clamp(tooHigh, caps).PowerLimitPercent);

        var tooLow  = new GpuControlState { PowerLimitPercent = 10, CoreClockOffsetMhz = 0, TempLimitC = 80 };
        Assert.Equal(50, GpuControlService.Clamp(tooLow, caps).PowerLimitPercent);
    }

    [Fact]
    public void Clamp_TempLimitClamped_60_To_95()
    {
        var caps = DefaultCaps();

        var tooHigh = new GpuControlState { TempLimitC = 120, PowerLimitPercent = 100 };
        Assert.Equal(95, GpuControlService.Clamp(tooHigh, caps).TempLimitC);

        var tooLow  = new GpuControlState { TempLimitC = 10, PowerLimitPercent = 100 };
        Assert.Equal(60, GpuControlService.Clamp(tooLow, caps).TempLimitC);
    }

    [Fact]
    public void Clamp_FanNullStaysNull()
    {
        var caps    = DefaultCaps();
        var desired = new GpuControlState { FanPercent = null, PowerLimitPercent = 100, TempLimitC = 80 };
        var clamped = GpuControlService.Clamp(desired, caps);
        Assert.Null(clamped.FanPercent);
    }

    [Fact]
    public void Clamp_FanValueClamped_0_To_100()
    {
        var caps = DefaultCaps();

        var over  = new GpuControlState { FanPercent = 150, PowerLimitPercent = 100, TempLimitC = 80 };
        Assert.Equal(100, GpuControlService.Clamp(over, caps).FanPercent);

        var under = new GpuControlState { FanPercent = -10, PowerLimitPercent = 100, TempLimitC = 80 };
        Assert.Equal(0, GpuControlService.Clamp(under, caps).FanPercent);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_ReturnsClamped_AndCallsBackendWithClampedValues()
    {
        string capturedError = "";
        GpuControlState? capturedState = null;

        var backend = MakeBackend(GpuVendor.Nvidia, isAvailable: true, applyOk: true);
        backend.Setup(b => b.TryApply(It.IsAny<GpuControlState>(), out capturedError))
               .Callback((GpuControlState s, ref string e) => { capturedState = s; })
               .Returns(true);

        var sensors = MakeSensors();
        var svc     = new GpuControlService(sensors.Object, new[] { backend.Object });

        // Send values that need clamping
        var desired = new GpuControlState
        {
            CoreClockOffsetMhz   = 999,  // should clamp to 300
            MemoryClockOffsetMhz = 0,
            PowerLimitPercent    = 200,  // should clamp to 120
            TempLimitC           = 120,  // should clamp to 95
        };

        var (ok, _, applied) = svc.Apply(desired);

        Assert.True(ok);
        Assert.Equal(300, applied.CoreClockOffsetMhz);
        Assert.Equal(120, applied.PowerLimitPercent);
        Assert.Equal(95,  applied.TempLimitC);
    }

    [Fact]
    public void Apply_WithNullBackend_ReturnsFalseWithReason_NoCrash()
    {
        var sensors = MakeSensors();
        var svc     = new GpuControlService(sensors.Object, Array.Empty<IGpuControlBackend>());

        var (ok, error, _) = svc.Apply(new GpuControlState { PowerLimitPercent = 100, TempLimitC = 80 });

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    // ── Telemetry ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReadTelemetry_MapsSensorSnapshot_ToGpuTelemetrySnapshot()
    {
        var sensors  = MakeSensors(gpuTempC: 72.0);
        var backend  = MakeBackend(GpuVendor.Nvidia, isAvailable: true);
        var svc      = new GpuControlService(sensors.Object, new[] { backend.Object });

        var snapshots = svc.ReadTelemetry();

        Assert.NotEmpty(snapshots);
        var snap = snapshots[0];
        Assert.Equal(72.0, snap.TemperatureC);
        Assert.Equal(1800, snap.CoreClockMhz);
        Assert.Equal(7000, snap.MemoryClockMhz);
        Assert.Equal(75,   snap.LoadPercent);
        Assert.Equal(200,  snap.PowerWatts);
    }

    [Fact]
    public void ReadTelemetry_WhenSensorsUnavailable_ReturnsEmpty_NoCrash()
    {
        var sensors = MakeSensors(isAvailable: false);
        var svc     = new GpuControlService(sensors.Object, new[] { new NullGpuBackend() });

        var snapshots = svc.ReadTelemetry();

        Assert.Empty(snapshots);
    }

    // ── Watchdog ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Watchdog_TempExceedsLimit_ResetsAndReturnsAbortedMessage()
    {
        // Sensors always return 95°C — well above any watchdog limit
        var sensors = MakeSensors(gpuTempC: 95.0);
        var backend = MakeBackend(GpuVendor.Nvidia, isAvailable: true, applyOk: true);

        var svc = new GpuControlService(sensors.Object, new[] { backend.Object });

        var desired = new GpuControlState { PowerLimitPercent = 100, TempLimitC = 83 };
        var result  = await svc.ApplyWithWatchdogAsync(
            desired,
            watchdogTempC: 85,      // limit = 85°C, sensor returns 95°C → should abort
            testDuration:  TimeSpan.FromSeconds(10),
            ct:            CancellationToken.None);

        Assert.Contains("watchdog aborted", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("95", result, StringComparison.Ordinal); // peak temp in message
        backend.Verify(b => b.ResetToDefault(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Watchdog_TempStaysUnder_CompletesWithoutReset()
    {
        // Sensors return 60°C — always under the watchdog limit of 85°C
        var sensors = MakeSensors(gpuTempC: 60.0);
        var backend = MakeBackend(GpuVendor.Nvidia, isAvailable: true, applyOk: true);

        var svc = new GpuControlService(sensors.Object, new[] { backend.Object });

        var desired = new GpuControlState { PowerLimitPercent = 100, TempLimitC = 83 };
        var result  = await svc.ApplyWithWatchdogAsync(
            desired,
            watchdogTempC: 85,
            testDuration:  TimeSpan.FromSeconds(2),   // short test for unit test speed
            ct:            CancellationToken.None);

        Assert.Contains("completed", result, StringComparison.OrdinalIgnoreCase);
        backend.Verify(b => b.ResetToDefault(), Times.Never);
    }

    // ── NullGpuBackend ────────────────────────────────────────────────────────

    [Fact]
    public void NullGpuBackend_AllPropertiesCorrect()
    {
        var backend = new NullGpuBackend();
        Assert.False(backend.IsAvailable);
        Assert.Equal(GpuVendor.Unknown, backend.Vendor);
        Assert.NotNull(backend.UnavailableReason);

        var caps = backend.GetCapabilities();
        Assert.False(caps.CanReadTelemetry);
        Assert.False(caps.CanSetCoreOffset);
        Assert.False(caps.CanSetMemoryOffset);
        Assert.False(caps.CanSetPowerLimit);
        Assert.False(caps.CanSetFan);

        // TryApply must return false, not throw
        Assert.False(backend.TryApply(new GpuControlState(), out var err));
        Assert.NotEmpty(err);

        // ResetToDefault must not throw
        backend.ResetToDefault();
    }

    // ── GPU memory (VRAM) unit handling ───────────────────────────────────────

    [Fact]
    public void GpuMemoryUsedMb_SmallDataReading_StaysInMb()
    {
        // LHM reports "GPU Memory Used" as SmallData (MB). It must not be scaled.
        var snap = new HardwareSnapshot();
        snap.GpuMemory.Add(new SensorReading
        {
            Name = "GPU Memory Used", HardwareName = "NVIDIA", Value = 4096,
            Kind = SensorKind.Data, Unit = "MB",
        });

        Assert.Equal(4096, snap.GpuMemoryUsedMb);
    }

    [Fact]
    public void GpuMemoryUsedMb_DataReadingInGb_IsConvertedToMb()
    {
        var snap = new HardwareSnapshot();
        snap.GpuMemory.Add(new SensorReading
        {
            Name = "GPU Memory Used", HardwareName = "AMD", Value = 4,
            Kind = SensorKind.Data, Unit = "GB",
        });

        Assert.Equal(4096, snap.GpuMemoryUsedMb); // 4 GB → 4096 MB
    }

    [Fact]
    public void GpuMemoryUsedMb_NoReading_IsNull()
    {
        Assert.Null(new HardwareSnapshot().GpuMemoryUsedMb);
    }

    [Fact]
    public void ReadTelemetry_MapsVramUsed_FromSmallDataMb_WithoutInflation()
    {
        // Regression: the old consumer assumed GB and multiplied by 1024, inflating SmallData
        // (MB) GPU memory 1024×. VRAM used should pass through as MB.
        var snap = new HardwareSnapshot();
        snap.GpuLoads.Add(new SensorReading { Name = "GPU Core", Value = 50, Kind = SensorKind.Load, Unit = "%" });
        snap.GpuMemory.Add(new SensorReading
        {
            Name = "GPU Memory Used", HardwareName = "NVIDIA GeForce Test", Value = 8192,
            Kind = SensorKind.Data, Unit = "MB",
        });
        var sensors = new Mock<ISensorService>();
        sensors.Setup(s => s.IsAvailable).Returns(true);
        sensors.Setup(s => s.GetSnapshot()).Returns(snap);
        var backend = MakeBackend(GpuVendor.Nvidia, isAvailable: true);

        var result = new GpuControlService(sensors.Object, new[] { backend.Object }).ReadTelemetry();

        Assert.NotEmpty(result);
        Assert.Equal(8192, result[0].VramUsedMb);
    }
}
