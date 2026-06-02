using System.CommandLine;
using System.Text.Json;

namespace Optimizer.Cli.Commands.Cloud;

public class CloudStatusCommand : Command
{
    public CloudStatusCommand() : base("status", "Show cloud server health and connection info")
    {
        this.SetHandler(async () =>
        {
            var api = CloudApiClient.FromEnv();

            var health = await api.GetAsync("/api/health");
            if (health == null)
            {
                Console.Error.WriteLine("Cloud server unreachable.");
                Environment.Exit(1);
            }

            var h = health.RootElement;
            var status  = h.TryGetProperty("status",  out var s)  ? s.GetString()  : "unknown";
            var version = h.TryGetProperty("version", out var v)  ? v.GetString()  : "unknown";
            var time    = h.TryGetProperty("time",    out var t)   ? t.GetString()  : "";

            Console.WriteLine("Optimizer Cloud Status");
            Console.WriteLine("──────────────────────");
            Console.WriteLine($"Status:       {status}");
            Console.WriteLine($"Version:      {version}");
            if (!string.IsNullOrEmpty(time))
                Console.WriteLine($"Server Time:  {time}");
            Console.WriteLine($"Server URL:   {Environment.GetEnvironmentVariable("OPTIMIZER_CLOUD_URL") ?? "http://localhost:5000"}");
            Console.WriteLine($"Auth:         API key (X-Api-Key)");
            Console.WriteLine();
            Console.WriteLine("Connection OK.");
        });
    }
}
