using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

/// <summary>
/// ISensorService backed by an EXTERNAL LibreHardwareMonitor web server (its /data.json
/// endpoint) instead of an in-process LHM instance. On machines where another component
/// already owns the LHM kernel driver (see docs/MACHINE-OWNERSHIP.md), a second in-process
/// driver stack causes contention — this implementation keeps the machine at exactly one
/// driver owner. There is deliberately NO fallback to in-process LHM: if the server is
/// down, sensors are unavailable until it returns (IsAvailable recovers automatically).
/// </summary>
public class ExternalLhmSensorService : ISensorService
{
    private readonly HttpClient _http;
    private readonly string _url;
    private bool _disposed;

    public bool IsAvailable { get; private set; }
    public string? InitializationError { get; private set; }

    public ExternalLhmSensorService(string url, HttpMessageHandler? handler = null)
    {
        _url = url;
        _http = handler == null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            using var probe = _http.GetAsync(_url).GetAwaiter().GetResult();
            probe.EnsureSuccessStatusCode();
            IsAvailable = true;
            EngineLog.Write($"[ExternalLhmSensorService] Using external LHM server at {_url}");
        }
        catch (Exception ex)
        {
            InitializationError = $"External LHM server unreachable at {_url}: {ex.Message}";
            IsAvailable = false;
            EngineLog.Error($"External LHM probe failed ({_url})", ex);
        }
    }

    public HardwareSnapshot GetSnapshot()
    {
        if (_disposed) return new HardwareSnapshot();
        var wasAvailable = IsAvailable;
        try
        {
            var json = _http.GetStringAsync(_url).GetAwaiter().GetResult();
            var snapshot = ParseSnapshot(json);
            IsAvailable = true;
            if (!wasAvailable) EngineLog.Write($"[ExternalLhmSensorService] Server back at {_url}");
            return snapshot;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            // Log state TRANSITIONS only — GetSnapshot runs on 1-15 s timers and a dead
            // server would otherwise flood the engine log.
            if (wasAvailable) EngineLog.Error("External LHM snapshot failed", ex);
            return new HardwareSnapshot();
        }
    }

    private enum Bucket { Cpu, Gpu, Storage, Other }

    /// <summary>
    /// Parses an LHM 0.9.6 /data.json document into a HardwareSnapshot, bucketing sensors
    /// exactly like the in-process SensorService does (CPU/GPU/Storage buckets; fan sensors
    /// on other hardware — e.g. motherboard SuperIO headers — land in FanSpeeds; NICs are
    /// skipped for parity with IsNetworkEnabled=false). Public static for unit testing.
    /// </summary>
    public static HardwareSnapshot ParseSnapshot(string json)
    {
        var snapshot = new HardwareSnapshot();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Children", out var computers) ||
            computers.ValueKind != JsonValueKind.Array) return snapshot;

        foreach (var computer in computers.EnumerateArray())
        {
            if (!computer.TryGetProperty("Children", out var hardwareNodes) ||
                hardwareNodes.ValueKind != JsonValueKind.Array) continue;
            foreach (var hw in hardwareNodes.EnumerateArray())
            {
                var image = GetString(hw, "ImageURL");
                var bucket = BucketFromImage(image);
                if (bucket == null) continue;
                CollectNode(hw, bucket.Value, GetString(hw, "Text"), snapshot);
            }
        }
        return snapshot;
    }

    // The web tree identifies hardware kind only by icon. cpu.png is the CPU; intel.png
    // only ever appears on Intel iGPUs (the CPU itself gets cpu.png).
    private static Bucket? BucketFromImage(string image) => image switch
    {
        "images_icon/cpu.png"       => Bucket.Cpu,
        "images_icon/nvidia.png"    => Bucket.Gpu,
        "images_icon/ati.png"       => Bucket.Gpu,
        "images_icon/intel.png"     => Bucket.Gpu,
        "images_icon/hdd.png"       => Bucket.Storage,
        "images_icon/mainboard.png" => Bucket.Other,
        "images_icon/ram.png"       => Bucket.Other,
        "images_icon/nic.png"       => null,
        _                           => Bucket.Other,
    };

    private static void CollectNode(JsonElement node, Bucket bucket, string hardwareName, HardwareSnapshot snapshot)
    {
        // Sensor leaves carry SensorId + Type; group nodes ("Temperatures", sub-chips) don't.
        if (node.TryGetProperty("SensorId", out var sid) && sid.ValueKind == JsonValueKind.String)
            AddReading(node, GetString(node, "Type"), bucket, hardwareName, snapshot);

        if (node.TryGetProperty("Children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                CollectNode(child, bucket, hardwareName, snapshot);
    }

    private static void AddReading(JsonElement node, string type, Bucket bucket, string hardwareName, HardwareSnapshot snapshot)
    {
        var reading = new SensorReading
        {
            Name = GetString(node, "Text"),
            HardwareName = hardwareName,
            Value = ParseNumber(node, "Value"),
            Min = ParseNumber(node, "Min"),
            Max = ParseNumber(node, "Max"),
        };

        (reading.Kind, reading.Unit) = type switch
        {
            "Temperature" => (SensorKind.Temperature, "°C"),
            "Clock"       => (SensorKind.Clock, "MHz"),
            "Voltage"     => (SensorKind.Voltage, "V"),
            "Fan"         => (SensorKind.Fan, "RPM"),
            "Load"        => (SensorKind.Load, "%"),
            "Power"       => (SensorKind.Power, "W"),
            "Data"        => (SensorKind.Data, "GB"),
            "SmallData"   => (SensorKind.Data, "MB"), // LHM reports SmallData in MB (e.g. GPU memory)
            _             => (SensorKind.Data, ""),
        };

        switch (bucket)
        {
            case Bucket.Cpu:
                switch (type)
                {
                    case "Temperature": snapshot.CpuTemperatures.Add(reading); break;
                    case "Clock":       snapshot.CpuClocks.Add(reading); break;
                    case "Load":        snapshot.CpuLoads.Add(reading); break;
                    case "Power":       snapshot.CpuPowers.Add(reading); break;
                    case "Voltage":     snapshot.Voltages.Add(reading); break;
                }
                break;
            case Bucket.Gpu:
                switch (type)
                {
                    case "Temperature": snapshot.GpuTemperatures.Add(reading); break;
                    case "Clock":       snapshot.GpuClocks.Add(reading); break;
                    case "Load":        snapshot.GpuLoads.Add(reading); break;
                    case "Power":       snapshot.GpuPowers.Add(reading); break;
                    case "Data":
                    case "SmallData":   snapshot.GpuMemory.Add(reading); break;
                    case "Fan":         snapshot.FanSpeeds.Add(reading); break;
                }
                break;
            case Bucket.Storage:
                if (type == "Temperature") snapshot.StorageTemperatures.Add(reading);
                break;
            case Bucket.Other:
                if (type == "Fan") snapshot.FanSpeeds.Add(reading);
                break;
        }
    }

    private static string GetString(JsonElement node, string property)
        => node.TryGetProperty(property, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? "" : "";

    // Values arrive culture-formatted with units ("98,8 W", "5478.0 MHz") and no thousands
    // grouping. Extract the first numeric token and normalize the decimal separator.
    private static double? ParseNumber(JsonElement node, string property)
    {
        var text = GetString(node, property);
        if (text.Length == 0) return null;
        var m = Regex.Match(text, @"-?\d+(?:[.,]\d+)?");
        if (!m.Success) return null;
        return double.Parse(m.Value.Replace(',', '.'), CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
