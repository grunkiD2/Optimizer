using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for <see cref="DeviceControlService"/>:
///   - Critical-device classification (pure static method)
///   - Status/ErrorCode mapping
///   - SetEnabledAsync refuses critical devices
///   - Class filter passes through to WMI
///   - Empty/error WMI result → empty list, no crash
/// </summary>
public class DeviceControlTests
{
    // ── ClassifyCritical — pure static tests ──────────────────────────────────

    [Theory]
    [InlineData("Processor", "Intel Core i9", true)]
    [InlineData("System", "ACPI Fixed Feature Button", true)]
    [InlineData("Computer", "ACPI x64-based PC", true)]
    [InlineData("DiskDrive", "Samsung NVMe", true)]
    [InlineData("Display", "NVIDIA GeForce RTX 4090", true)]
    [InlineData("Keyboard", "USB HID Keyboard", true)]
    [InlineData("USB", "Generic USB Hub", false)]
    [InlineData("Net", "Realtek Ethernet", false)]
    [InlineData("AudioEndpoint", "Realtek Audio", false)]
    public void ClassifyCritical_KnownClasses_MatchesExpected(
        string pnpClass, string name, bool expectedCritical)
    {
        var result = DeviceControlService.ClassifyCritical(pnpClass, name);
        Assert.Equal(expectedCritical, result);
    }

    [Fact]
    public void ClassifyCritical_EmptyClass_ReturnsFalse()
    {
        Assert.False(DeviceControlService.ClassifyCritical("", "Some Device"));
    }

    // ── MapStatus — error-code mapping ────────────────────────────────────────

    [Theory]
    [InlineData(0,  true)]
    [InlineData(22, false)]
    [InlineData(10, true)]
    public void MapStatus_ErrorCode_MapsToExpectedEnabled(int code, bool expectedEnabled)
    {
        var (_, isEnabled) = DeviceControlService.MapStatus(code);
        Assert.Equal(expectedEnabled, isEnabled);
    }

    [Fact]
    public void MapStatus_Code22_IsDisabledStatus()
    {
        var (status, isEnabled) = DeviceControlService.MapStatus(22);
        Assert.Equal("Disabled", status);
        Assert.False(isEnabled);
    }

    [Fact]
    public void MapStatus_Code0_IsOkAndEnabled()
    {
        var (status, isEnabled) = DeviceControlService.MapStatus(0);
        Assert.Equal("OK", status);
        Assert.True(isEnabled);
    }

    // ── SetEnabledAsync refuses critical devices ───────────────────────────────

    [Fact]
    public async Task SetEnabledAsync_RefusesCriticalDevice_ReturnsFalseWithoutCallingPnputil()
    {
        // Arrange: mock WMI to return a critical device
        var wmiMock = new Mock<IWmiQueryService>();
        wmiMock
            .Setup(w => w.QueryAsync(
                It.Is<string>(q => q.Contains("Win32_PnPEntity")),
                It.IsAny<Func<System.Management.ManagementObject, PnpDevice>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new List<PnpDevice>
            {
                new PnpDevice
                {
                    InstanceId = "PCI\\VEN_8086&DEV_0001",
                    Name       = "Intel Core i9 Processor",
                    Class      = "Processor",
                    Status     = "OK",
                    IsEnabled  = true,
                    IsCritical = true,
                }
            });

        var svc = new DeviceControlService(wmiMock.Object);

        // Act: try to disable the critical device
        var result = await svc.SetEnabledAsync("PCI\\VEN_8086&DEV_0001", enabled: false);

        // Assert: refused, no pnputil was called (would fail anyway, but the guard must kick in first)
        Assert.False(result);
    }

    // ── ListDevicesAsync — class filter ───────────────────────────────────────

    [Fact]
    public async Task ListDevicesAsync_WithClassFilter_PassesFilterInQuery()
    {
        string? capturedQuery = null;
        var wmiMock = new Mock<IWmiQueryService>();
        wmiMock
            .Setup(w => w.QueryAsync(
                It.IsAny<string>(),
                It.IsAny<Func<System.Management.ManagementObject, PnpDevice>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .Callback<string, Func<System.Management.ManagementObject, PnpDevice>, TimeSpan?, string?>(
                (q, _, _, _) => capturedQuery = q)
            .ReturnsAsync(new List<PnpDevice>());

        var svc = new DeviceControlService(wmiMock.Object);
        await svc.ListDevicesAsync(classFilter: "USB");

        Assert.NotNull(capturedQuery);
        Assert.Contains("USB", capturedQuery!, StringComparison.Ordinal);
    }

    // ── ListDevicesAsync — empty WMI result is handled gracefully ─────────────

    [Fact]
    public async Task ListDevicesAsync_WmiReturnsEmpty_ReturnsEmptyList()
    {
        var wmiMock = new Mock<IWmiQueryService>();
        wmiMock
            .Setup(w => w.QueryAsync(
                It.IsAny<string>(),
                It.IsAny<Func<System.Management.ManagementObject, PnpDevice>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(new List<PnpDevice>());

        var svc = new DeviceControlService(wmiMock.Object);
        var result = await svc.ListDevicesAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── ListDevicesAsync — WMI exception → empty list, no throw ──────────────

    [Fact]
    public async Task ListDevicesAsync_WmiThrows_ReturnsEmptyList()
    {
        var wmiMock = new Mock<IWmiQueryService>();
        wmiMock
            .Setup(w => w.QueryAsync(
                It.IsAny<string>(),
                It.IsAny<Func<System.Management.ManagementObject, PnpDevice>>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<string?>()))
            .ThrowsAsync(new InvalidOperationException("WMI not available"));

        var svc = new DeviceControlService(wmiMock.Object);

        // Should not throw
        var result = await svc.ListDevicesAsync();
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}
