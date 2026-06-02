using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for DiskHealthService JSON parsing logic.
/// Instead of running PowerShell, we mock IPowerShellRunner to return
/// sample JSON that mirrors real Get-PhysicalDisk output.
/// </summary>
public class DiskHealthServiceTests
{
    private static Mock<IPowerShellRunner> BuildRunnerMock(string? output)
    {
        var mock = new Mock<IPowerShellRunner>();
        mock.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(output);
        return mock;
    }

    // Compact single-disk JSON (as PowerShell ConvertTo-Json -Compress produces)
    private const string SingleDiskJson =
        "{\"Model\":\"Samsung SSD 870 QVO\",\"SerialNumber\":\"ABC123\",\"BusType\":11,\"MediaType\":4,\"Size\":1000204886016,\"HealthStatus\":0,\"OperationalStatus\":\"OK\",\"Temperature\":35,\"Wear\":5,\"PowerOnHours\":8760,\"StartStopCycleCount\":100,\"ReadErrorsTotal\":0,\"WriteErrorsTotal\":0}";

    // Both disks have no null fields — HDDs may have no wear data so it's simply omitted
    private const string MultiDiskJson =
        "[{\"Model\":\"WD Blue 1TB\",\"SerialNumber\":\"XYZ789\",\"BusType\":11,\"MediaType\":3,\"Size\":1000204886016,\"HealthStatus\":0,\"OperationalStatus\":\"OK\",\"Temperature\":38,\"PowerOnHours\":12000,\"StartStopCycleCount\":500,\"ReadErrorsTotal\":0,\"WriteErrorsTotal\":0},{\"Model\":\"Samsung 980 Pro\",\"SerialNumber\":\"NVMeABC\",\"BusType\":17,\"MediaType\":4,\"Size\":2000398934016,\"HealthStatus\":0,\"OperationalStatus\":\"OK\",\"Temperature\":42,\"Wear\":3,\"PowerOnHours\":4380,\"StartStopCycleCount\":50,\"ReadErrorsTotal\":0,\"WriteErrorsTotal\":0}]";

    [Fact]
    public async Task GetDiskHealthAsync_NullOutput_ReturnsEmptyList()
    {
        var runner = BuildRunnerMock(null);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDiskHealthAsync_EmptyOutput_ReturnsEmptyList()
    {
        var runner = BuildRunnerMock("   ");
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDiskHealthAsync_SingleDiskJson_ParsesCorrectly()
    {
        var runner = BuildRunnerMock(SingleDiskJson);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Single(result);
        var disk = result[0];
        Assert.Equal("Samsung SSD 870 QVO", disk.Model);
        Assert.Equal("ABC123", disk.SerialNumber);
        Assert.Equal("SATA", disk.BusType);    // BusType 11 = SATA
        Assert.Equal("SSD", disk.MediaType);   // MediaType 4 = SSD
        Assert.Equal(35, disk.TemperatureCelsius);
        Assert.Equal(5, disk.WearPercentage);
        Assert.Equal("Healthy", disk.HealthStatus);
        Assert.False(disk.IsPredictedToFail);
    }

    [Fact]
    public async Task GetDiskHealthAsync_MultiDiskJson_ReturnsBothDisks()
    {
        var runner = BuildRunnerMock(MultiDiskJson);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, d => d.MediaType == "HDD");
        Assert.Contains(result, d => d.BusType == "NVMe");
    }

    [Fact]
    public async Task GetDiskHealthAsync_NvmeDisk_ParsesBusTypeCorrectly()
    {
        var runner = BuildRunnerMock(MultiDiskJson);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        var nvme = result.First(d => d.BusType == "NVMe");
        Assert.Equal("Samsung 980 Pro", nvme.Model);
        Assert.Equal("SSD", nvme.MediaType);
    }

    [Fact]
    public async Task GetDiskHealthAsync_HddDisk_MissingWear_IsNull()
    {
        var runner = BuildRunnerMock(MultiDiskJson);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        var hdd = result.First(d => d.MediaType == "HDD");
        Assert.Null(hdd.WearPercentage); // HDD doesn't report wear — field omitted from JSON
    }

    [Fact]
    public async Task GetDiskHealthAsync_UnhealthyDisk_SetsPredictedToFail()
    {
        // HealthStatus 2 = Unhealthy — no null fields in this test JSON
        const string json =
            "{\"Model\":\"Dying HDD\",\"SerialNumber\":\"BAD001\",\"BusType\":11,\"MediaType\":3,\"Size\":500000000000,\"HealthStatus\":2,\"OperationalStatus\":\"OK\",\"Temperature\":60,\"Wear\":5,\"PowerOnHours\":50000,\"StartStopCycleCount\":5000,\"ReadErrorsTotal\":100,\"WriteErrorsTotal\":50}";

        var runner = BuildRunnerMock(json);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Single(result);
        Assert.Equal("Unhealthy", result[0].HealthStatus);
        Assert.True(result[0].IsPredictedToFail);
    }

    [Fact]
    public async Task GetDiskHealthAsync_PredictiveFailureStatus_SetsPredictedToFail()
    {
        // Omit optional fields rather than setting to null
        const string json =
            "{\"Model\":\"Failing Drive\",\"SerialNumber\":\"FAIL01\",\"BusType\":11,\"MediaType\":3,\"Size\":500000000000,\"HealthStatus\":1,\"OperationalStatus\":\"Predictive Failure\",\"ReadErrorsTotal\":0,\"WriteErrorsTotal\":0}";

        var runner = BuildRunnerMock(json);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Single(result);
        Assert.True(result[0].IsPredictedToFail);
    }

    [Fact]
    public async Task GetDiskHealthAsync_InvalidJson_ReturnsEmptyList()
    {
        var runner = BuildRunnerMock("{ this is not valid json ~~~");
        var service = new DiskHealthService(runner.Object);

        // Should not throw — gracefully returns empty
        var result = await service.GetDiskHealthAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetDiskHealthAsync_MissingOptionalFields_HandledGracefully()
    {
        // When optional fields are missing (omitted from JSON), they should be null
        const string json =
            "{\"Model\":\"Basic Disk\",\"SerialNumber\":\"SER001\",\"BusType\":0,\"MediaType\":0,\"Size\":0,\"HealthStatus\":0,\"OperationalStatus\":\"OK\"}";

        var runner = BuildRunnerMock(json);
        var service = new DiskHealthService(runner.Object);

        var result = await service.GetDiskHealthAsync();

        Assert.Single(result);
        Assert.Null(result[0].TemperatureCelsius);   // not in JSON → null
        Assert.Null(result[0].WearPercentage);        // not in JSON → null
        Assert.Null(result[0].PowerOnHours);          // not in JSON → null
    }
}
