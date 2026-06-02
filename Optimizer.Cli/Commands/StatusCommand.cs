using System.CommandLine;
using System.Text.Json;

namespace Optimizer.Cli;

public class StatusCommand : Command
{
    public StatusCommand() : base("status", "Show current system metrics")
    {
        this.SetHandler(async () =>
        {
            var api = ApiClient.FromEnv();

            if (!await api.IsHealthyAsync())
            {
                Console.Error.WriteLine("Cannot reach Optimizer API. Is the GUI running with Remote API enabled?");
                Environment.Exit(1);
            }

            var metrics = await api.GetAsync("/api/metrics");
            if (metrics == null) return;

            var m = metrics.RootElement;
            Console.WriteLine("Optimizer Status");
            Console.WriteLine("─────────────────");
            Console.WriteLine($"CPU:     {m.GetProperty("cpu").GetDouble():F1}%");

            var mem = m.GetProperty("memory");
            var total = mem.GetProperty("total").GetInt64();
            var avail = mem.GetProperty("available").GetInt64();
            var used = total - avail;
            Console.WriteLine($"Memory:  {100.0 * used / total:F1}% ({used / 1024 / 1024 / 1024} GB used of {total / 1024 / 1024 / 1024} GB)");

            Console.WriteLine($"GPU:     {m.GetProperty("gpu").GetDouble():F1}%");
            Console.WriteLine($"Disk:    {m.GetProperty("disk").GetDouble():F1}%");

            var sensors = await api.GetAsync("/api/sensors");
            if (sensors != null)
            {
                Console.WriteLine();
                Console.WriteLine("Sensors");
                Console.WriteLine("───────");
                if (sensors.RootElement.TryGetProperty("cpuTemp", out var ct) && ct.ValueKind == JsonValueKind.Number)
                    Console.WriteLine($"CPU Temp: {ct.GetDouble():F0}°C");
                if (sensors.RootElement.TryGetProperty("gpuTemp", out var gt) && gt.ValueKind == JsonValueKind.Number)
                    Console.WriteLine($"GPU Temp: {gt.GetDouble():F0}°C");
            }
        });
    }
}
