using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Diagnostics;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for diagnostic plugins with mocked dependencies.
/// </summary>
public class DiagnosticPluginsTests
{
    // ── MemoryUsagePlugin ─────────────────────────────────────────────────────

    private static Mock<ISystemMonitorService> MonitorWith(long totalBytes, long availableBytes)
    {
        var mock = new Mock<ISystemMonitorService>();
        mock.Setup(m => m.CollectSnapshot()).Returns(new SystemResource
        {
            TotalPhysicalMemory = totalBytes,
            AvailablePhysicalMemory = availableBytes
        });
        return mock;
    }

    [Fact]
    public async Task MemoryUsagePlugin_BelowThreshold_ReturnsNoFindings()
    {
        // 50% usage — below 90% threshold
        long total = 8L * 1024 * 1024 * 1024;
        long available = total / 2;
        var monitor = MonitorWith(total, available);
        var plugin = new MemoryUsagePlugin(monitor.Object);

        var findings = await plugin.RunAsync();

        Assert.Empty(findings);
    }

    [Fact]
    public async Task MemoryUsagePlugin_AboveThreshold_ReturnsFinding()
    {
        // 95% usage — above 90% threshold
        long total = 8L * 1024 * 1024 * 1024;
        long available = (long)(total * 0.05); // only 5% free
        var monitor = MonitorWith(total, available);
        var plugin = new MemoryUsagePlugin(monitor.Object);

        var findings = await plugin.RunAsync();

        Assert.Single(findings);
        Assert.Equal("mem-high", findings[0].Id);
        Assert.Equal(FindingSeverity.Warning, findings[0].Severity);
    }

    [Fact]
    public async Task MemoryUsagePlugin_ZeroTotal_ReturnsNoFindings()
    {
        // Guard against division by zero — zero total memory returns nothing
        var monitor = MonitorWith(0, 0);
        var plugin = new MemoryUsagePlugin(monitor.Object);

        var findings = await plugin.RunAsync();

        Assert.Empty(findings);
    }

    // ── UptimePlugin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task UptimePlugin_RunAsync_DoesNotThrow()
    {
        var plugin = new UptimePlugin();
        var findings = await plugin.RunAsync();
        Assert.NotNull(findings);
    }

    [Fact]
    public void UptimePlugin_SupportedLevels_IsBoth()
    {
        var plugin = new UptimePlugin();
        Assert.Equal(DiagnosticScanLevel.Both, plugin.SupportedLevels);
    }

    [Fact]
    public void UptimePlugin_Name_IsPopulated()
    {
        var plugin = new UptimePlugin();
        Assert.False(string.IsNullOrWhiteSpace(plugin.Name));
    }

    // ── BootTimePlugin ────────────────────────────────────────────────────────

    private static Mock<IBootAnalysisService> BootWith(IReadOnlyList<BootMetrics> records)
    {
        var mock = new Mock<IBootAnalysisService>();
        mock.Setup(b => b.GetBootHistoryAsync(It.IsAny<int>())).ReturnsAsync(records);
        return mock;
    }

    [Fact]
    public async Task BootTimePlugin_AllBootsFast_ReturnsNoFindings()
    {
        var records = new List<BootMetrics>
        {
            new() { BootDuration = TimeSpan.FromSeconds(30) },
            new() { BootDuration = TimeSpan.FromSeconds(45) }
        };
        var boot = BootWith(records);
        var plugin = new BootTimePlugin(boot.Object);

        var findings = await plugin.RunAsync();

        Assert.Empty(findings);
    }

    [Fact]
    public async Task BootTimePlugin_SlowBoot_ReturnsFinding()
    {
        var records = new List<BootMetrics>
        {
            new() { BootDuration = TimeSpan.FromSeconds(90) },
            new() { BootDuration = TimeSpan.FromSeconds(120) }
        };
        var boot = BootWith(records);
        var plugin = new BootTimePlugin(boot.Object);

        var findings = await plugin.RunAsync();

        Assert.Single(findings);
        Assert.Equal("boot-slow", findings[0].Id);
        Assert.Equal(FindingSeverity.Warning, findings[0].Severity);
    }

    [Fact]
    public async Task BootTimePlugin_EmptyHistory_ReturnsNoFindings()
    {
        var boot = BootWith(new List<BootMetrics>());
        var plugin = new BootTimePlugin(boot.Object);

        var findings = await plugin.RunAsync();

        Assert.Empty(findings);
    }

    // ── HardwareSpecsPlugin ───────────────────────────────────────────────────

    private static Mock<IHardwareInfoService> HwWith(long ramBytes, bool secureBoot)
    {
        var mock = new Mock<IHardwareInfoService>();
        mock.Setup(h => h.GetHardwareInfoAsync()).ReturnsAsync(new HardwareInfo
        {
            Memory = new MemoryHardwareInfo { TotalBytes = ramBytes },
            Os = new OsInfo { IsSecureBoot = secureBoot }
        });
        return mock;
    }

    [Fact]
    public async Task HardwareSpecsPlugin_SufficientRam_NoRamFinding()
    {
        var hw = HwWith(16L * 1_073_741_824L, secureBoot: true);
        var plugin = new HardwareSpecsPlugin(hw.Object);

        var findings = await plugin.RunAsync();

        Assert.DoesNotContain(findings, f => f.Id == "ram-low");
    }

    [Fact]
    public async Task HardwareSpecsPlugin_LowRam_ReturnsFinding()
    {
        var hw = HwWith(4L * 1_073_741_824L, secureBoot: true);
        var plugin = new HardwareSpecsPlugin(hw.Object);

        var findings = await plugin.RunAsync();

        Assert.Contains(findings, f => f.Id == "ram-low");
    }

    [Fact]
    public async Task HardwareSpecsPlugin_SecureBootDisabled_ReturnsFinding()
    {
        var hw = HwWith(16L * 1_073_741_824L, secureBoot: false);
        var plugin = new HardwareSpecsPlugin(hw.Object);

        var findings = await plugin.RunAsync();

        Assert.Contains(findings, f => f.Id == "secureboot-off");
    }

    [Fact]
    public async Task HardwareSpecsPlugin_GoodSpecs_ReturnsNoFindings()
    {
        var hw = HwWith(16L * 1_073_741_824L, secureBoot: true);
        var plugin = new HardwareSpecsPlugin(hw.Object);

        var findings = await plugin.RunAsync();

        Assert.Empty(findings);
    }

    // ── DiskSpacePlugin ───────────────────────────────────────────────────────

    [Fact]
    public async Task DiskSpacePlugin_RunAsync_DoesNotThrow()
    {
        var plugin = new DiskSpacePlugin();
        var findings = await plugin.RunAsync();
        Assert.NotNull(findings);
    }

    [Fact]
    public void DiskSpacePlugin_SupportedLevels_IsBoth()
    {
        var plugin = new DiskSpacePlugin();
        Assert.Equal(DiagnosticScanLevel.Both, plugin.SupportedLevels);
    }
}
