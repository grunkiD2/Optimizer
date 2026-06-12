using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ExternalLhmSensorServiceTests
{
    // Trimmed but structurally faithful copy of a real LHM 0.9.6 /data.json
    // (da-DK culture: comma decimals, units inside the Value strings, sensor
    // leaves carry SensorId + Type, group nodes don't).
    private const string SampleJson = """
    {
      "id": 0, "Text": "Sensor", "Children": [
        {
          "id": 1, "Text": "DIERS", "Children": [
            {
              "id": 2, "Text": "ASUS ROG MAXIMUS Z790 HERO", "ImageURL": "images_icon/mainboard.png", "Children": [
                {
                  "id": 3, "Text": "Nuvoton NCT6798D", "ImageURL": "images_icon/chip.png", "Children": [
                    {
                      "id": 4, "Text": "Fans", "Children": [
                        { "id": 5, "Text": "Fan #1", "Children": [], "Min": "0 RPM", "Value": "247 RPM", "Max": "1100 RPM", "SensorId": "/lpc/nct6798d/fan/0", "Type": "Fan" }
                      ]
                    },
                    {
                      "id": 6, "Text": "Temperatures", "Children": [
                        { "id": 7, "Text": "CPU Socket", "Children": [], "Min": "25,0 °C", "Value": "38,5 °C", "Max": "52,0 °C", "SensorId": "/lpc/nct6798d/temperature/1", "Type": "Temperature" }
                      ]
                    }
                  ]
                }
              ]
            },
            {
              "id": 10, "Text": "Intel Core i9-14900K", "ImageURL": "images_icon/cpu.png", "Children": [
                {
                  "id": 11, "Text": "Powers", "Children": [
                    { "id": 12, "Text": "CPU Package", "Children": [], "Min": "18,7 W", "Value": "98,8 W", "Max": "212,6 W", "SensorId": "/intelcpu/0/power/0", "Type": "Power" }
                  ]
                },
                {
                  "id": 13, "Text": "Temperatures", "Children": [
                    { "id": 14, "Text": "Core Max", "Children": [], "Min": "31,0 °C", "Value": "62,0 °C", "Max": "84,0 °C", "SensorId": "/intelcpu/0/temperature/0", "Type": "Temperature" },
                    { "id": 15, "Text": "CPU Package", "Children": [], "Min": "32,0 °C", "Value": "63,0 °C", "Max": "85,0 °C", "SensorId": "/intelcpu/0/temperature/26", "Type": "Temperature" }
                  ]
                },
                {
                  "id": 16, "Text": "Load", "Children": [
                    { "id": 17, "Text": "CPU Total", "Children": [], "Min": "0,9 %", "Value": "10,9 %", "Max": "82,1 %", "SensorId": "/intelcpu/0/load/0", "Type": "Load" }
                  ]
                },
                {
                  "id": 18, "Text": "Clocks", "Children": [
                    { "id": 19, "Text": "P-Core #1", "Children": [], "Min": "796,8 MHz", "Value": "5478,0 MHz", "Max": "5478,0 MHz", "SensorId": "/intelcpu/0/clock/1", "Type": "Clock" }
                  ]
                },
                {
                  "id": 20, "Text": "Voltages", "Children": [
                    { "id": 21, "Text": "CPU Core", "Children": [], "Min": "0,706 V", "Value": "1,371 V", "Max": "1,404 V", "SensorId": "/intelcpu/0/voltage/0", "Type": "Voltage" }
                  ]
                }
              ]
            },
            {
              "id": 30, "Text": "NVIDIA GeForce RTX 5080", "ImageURL": "images_icon/nvidia.png", "Children": [
                {
                  "id": 31, "Text": "Temperatures", "Children": [
                    { "id": 32, "Text": "GPU Core", "Children": [], "Min": "35,0 °C", "Value": "49,8 °C", "Max": "64,3 °C", "SensorId": "/gpu-nvidia/0/temperature/0", "Type": "Temperature" }
                  ]
                },
                {
                  "id": 33, "Text": "Fans", "Children": [
                    { "id": 34, "Text": "GPU Fan 1", "Children": [], "Min": "0 RPM", "Value": "1100 RPM", "Max": "2200 RPM", "SensorId": "/gpu-nvidia/0/fan/0", "Type": "Fan" }
                  ]
                },
                {
                  "id": 35, "Text": "Data", "Children": [
                    { "id": 36, "Text": "GPU Memory Used", "Children": [], "Min": "800,0 MB", "Value": "8540,5 MB", "Max": "15233,0 MB", "SensorId": "/gpu-nvidia/0/smalldata/0", "Type": "SmallData" }
                  ]
                },
                {
                  "id": 37, "Text": "Powers", "Children": [
                    { "id": 38, "Text": "GPU Package", "Children": [], "Min": "12,0 W", "Value": "67,9 W", "Max": "314,2 W", "SensorId": "/gpu-nvidia/0/power/0", "Type": "Power" }
                  ]
                }
              ]
            },
            {
              "id": 40, "Text": "Intel(R) UHD Graphics", "ImageURL": "images_icon/intel.png", "Children": [
                {
                  "id": 41, "Text": "Load", "Children": [
                    { "id": 42, "Text": "D3D 3D", "Children": [], "Min": "0,0 %", "Value": "1,2 %", "Max": "9,0 %", "SensorId": "/gpu-intel/0/load/0", "Type": "Load" }
                  ]
                }
              ]
            },
            {
              "id": 50, "Text": "Samsung SSD 970 EVO 1TB", "ImageURL": "images_icon/hdd.png", "Children": [
                {
                  "id": 51, "Text": "Temperatures", "Children": [
                    { "id": 52, "Text": "Temperature", "Children": [], "Min": "28,0 °C", "Value": "41,0 °C", "Max": "55,0 °C", "SensorId": "/nvme/0/temperature/0", "Type": "Temperature" }
                  ]
                }
              ]
            },
            {
              "id": 60, "Text": "Ethernet", "ImageURL": "images_icon/nic.png", "Children": [
                {
                  "id": 61, "Text": "Load", "Children": [
                    { "id": 62, "Text": "Network Utilization", "Children": [], "Min": "0,0 %", "Value": "0,3 %", "Max": "2,1 %", "SensorId": "/nic/0/load/0", "Type": "Load" }
                  ]
                }
              ]
            }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Parses_cpu_sensors_with_comma_decimals_and_units()
    {
        var snap = ExternalLhmSensorService.ParseSnapshot(SampleJson);

        Assert.Equal(98.8, snap.CpuPowerWatts);            // "CPU Package" power
        Assert.Equal(63.0, snap.CpuPackageTemperatureC);   // prefers the "Package" temp
        Assert.Equal(10.9, snap.CpuLoads.Single().Value);
        Assert.Equal(5478.0, snap.CpuClocks.Single().Value);
        Assert.Equal(1.371, snap.Voltages.Single().Value);
        Assert.Equal(18.7, snap.CpuPowers.Single().Min);
        Assert.Equal(212.6, snap.CpuPowers.Single().Max);
    }

    [Fact]
    public void Parses_gpu_sensors_including_memory_and_fan()
    {
        var snap = ExternalLhmSensorService.ParseSnapshot(SampleJson);

        Assert.Equal(49.8, snap.GpuTemperatureC);
        Assert.Equal(67.9, snap.GpuPowerWatts);
        Assert.Equal(8540.5, snap.GpuMemoryUsedMb);        // SmallData stays MB
        Assert.Contains(snap.FanSpeeds, f => f.Name == "GPU Fan 1" && f.Value == 1100);
        Assert.Equal("MB", snap.GpuMemory.Single().Unit);
    }

    [Fact]
    public void Discrete_gpu_wins_over_igpu_in_document_order()
    {
        var snap = ExternalLhmSensorService.ParseSnapshot(SampleJson);

        // The iGPU contributes loads but the RTX comes first in the tree, so the
        // convenience accessors (FirstOrDefault) resolve to the discrete GPU.
        Assert.Equal(2, snap.GpuLoads.Count + snap.GpuTemperatures.Count);
        Assert.Equal("NVIDIA GeForce RTX 5080", snap.GpuTemperatures.Single().HardwareName);
        Assert.Contains(snap.GpuLoads, l => l.HardwareName == "Intel(R) UHD Graphics");
    }

    [Fact]
    public void Motherboard_subchip_fans_land_in_FanSpeeds_and_its_temps_are_dropped()
    {
        var snap = ExternalLhmSensorService.ParseSnapshot(SampleJson);

        // Parity with in-proc SensorService: non-CPU/GPU/Storage hardware only contributes Fan sensors.
        Assert.Contains(snap.FanSpeeds, f => f.Name == "Fan #1" && f.Value == 247 && f.HardwareName == "ASUS ROG MAXIMUS Z790 HERO");
        Assert.DoesNotContain(snap.CpuTemperatures, t => t.Name == "CPU Socket");
        Assert.DoesNotContain(snap.StorageTemperatures, t => t.Name == "CPU Socket");
    }

    [Fact]
    public void Storage_temperature_is_bucketed_and_nic_is_skipped()
    {
        var snap = ExternalLhmSensorService.ParseSnapshot(SampleJson);

        Assert.Equal(41.0, snap.StorageTemperatures.Single().Value);
        Assert.DoesNotContain(snap.CpuLoads, l => l.Name == "Network Utilization");
        Assert.DoesNotContain(snap.GpuLoads, l => l.Name == "Network Utilization");
    }

    [Fact]
    public void Empty_or_malformed_payload_yields_empty_snapshot()
    {
        Assert.Empty(ExternalLhmSensorService.ParseSnapshot("{}").CpuTemperatures);
        Assert.Empty(ExternalLhmSensorService.ParseSnapshot("""{"Children": []}""").FanSpeeds);
    }

    private sealed class StubHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(responder());
    }

    [Fact]
    public void Service_is_available_and_serves_snapshots_when_server_responds()
    {
        using var svc = new ExternalLhmSensorService("http://localhost:9/data.json",
            new StubHandler(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleJson) }));

        Assert.True(svc.IsAvailable);
        Assert.Null(svc.InitializationError);
        Assert.Equal(98.8, svc.GetSnapshot().CpuPowerWatts);
    }

    [Fact]
    public void Dead_server_sets_error_and_recovers_when_it_returns()
    {
        var alive = false;
        using var svc = new ExternalLhmSensorService("http://localhost:9/data.json",
            new StubHandler(() => alive
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SampleJson) }
                : throw new HttpRequestException("connection refused")));

        Assert.False(svc.IsAvailable);
        Assert.NotNull(svc.InitializationError);
        Assert.Empty(svc.GetSnapshot().CpuPowers);   // still down → empty snapshot, no throw

        alive = true;                                // server comes back
        Assert.Equal(98.8, svc.GetSnapshot().CpuPowerWatts);
        Assert.True(svc.IsAvailable);
    }
}
