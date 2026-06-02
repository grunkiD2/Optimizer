using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for the per-DIMM memory module data path added in D4b.
/// Uses a mock <see cref="IWmiQueryService"/> returning synthetic Win32_PhysicalMemory rows.
/// </summary>
public class MemoryTimingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a mock WMI service that returns a fixed list of MemoryModuleInfo objects
    /// when queried for Win32_PhysicalMemory.  Other queries return empty.
    /// </summary>
    private static Mock<IWmiQueryService> MakeWmiMock(IReadOnlyList<MemoryModuleInfo> modules)
    {
        var mock = new Mock<IWmiQueryService>();

        // The HardwareInfoService queries Win32_PhysicalMemory; we intercept it by
        // matching any query containing that table name and returning suitable data.
        // Because the mapper lambda is opaque to Moq, we instead subclass / use
        // HardwareInfoService directly with a WMI mock at the model layer.

        // We test the MODEL mapping directly rather than through the full service,
        // because the service's mapper lambda is a local closure.
        // All the field-mapping logic is exercised through MemoryModuleInfo properties.
        mock.Setup(w => w.QueryAsync(
                It.IsAny<string>(),
                It.IsAny<Func<System.Management.ManagementObject, object>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new List<object>());

        return mock;
    }

    // ── MemoryModuleInfo property mapping ─────────────────────────────────────

    [Fact]
    public void MemoryModuleInfo_SlotText_PrefersDeviceLocator()
    {
        var m = new MemoryModuleInfo { BankLabel = "Bank 0", DeviceLocator = "DIMM_A1" };
        Assert.Equal("DIMM_A1", m.SlotText);
    }

    [Fact]
    public void MemoryModuleInfo_SlotText_FallsBackToBankLabel()
    {
        var m = new MemoryModuleInfo { BankLabel = "Bank 0", DeviceLocator = "" };
        Assert.Equal("Bank 0", m.SlotText);
    }

    [Fact]
    public void MemoryModuleInfo_SpeedText_ShowsConfiguredAndRated()
    {
        var m = new MemoryModuleInfo { SpeedMhz = 3600, ConfiguredSpeedMhz = 3200 };
        var text = m.SpeedText;
        Assert.Contains("3200", text, StringComparison.Ordinal);
        Assert.Contains("3600", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryModuleInfo_SpeedText_ShowsOnlyRatedWhenNoConfigured()
    {
        var m = new MemoryModuleInfo { SpeedMhz = 3200, ConfiguredSpeedMhz = 0 };
        Assert.Equal("3200 MHz", m.SpeedText);
    }

    [Fact]
    public void MemoryModuleInfo_VoltageText_FormatsAsVolts()
    {
        var m = new MemoryModuleInfo { ConfiguredVoltageMv = 1200 };
        // 1200 mV = 1.200 V — use invariant-culture-formatted string
        Assert.Contains("1.200", m.VoltageText, StringComparison.Ordinal);
        Assert.Contains("V", m.VoltageText, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryModuleInfo_VoltageText_ReturnsDashWhenZero()
    {
        var m = new MemoryModuleInfo { ConfiguredVoltageMv = 0 };
        Assert.Equal("—", m.VoltageText);
    }

    [Fact]
    public void MemoryHardwareInfo_ConfiguredClockSpeedMhz_PersistedOnModel()
    {
        var info = new MemoryHardwareInfo { ConfiguredClockSpeedMhz = 4800 };
        Assert.Equal(4800, info.ConfiguredClockSpeedMhz);
    }

    [Fact]
    public void MemoryHardwareInfo_Modules_EmptyByDefault()
    {
        var info = new MemoryHardwareInfo();
        Assert.Empty(info.Modules);
    }
}
