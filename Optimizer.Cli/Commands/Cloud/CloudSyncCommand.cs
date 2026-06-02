using System.CommandLine;

namespace Optimizer.Cli.Commands.Cloud;

public class CloudSyncCommand : Command
{
    public CloudSyncCommand() : base("sync", "Pull sync items from the cloud server and report counts")
    {
        var sinceOption = new Option<long>("--since", () => 0, "Cursor version to pull changes since");
        AddOption(sinceOption);

        this.SetHandler(async (long since) =>
        {
            var api = CloudApiClient.FromEnv();

            Console.WriteLine("Pulling sync items from cloud server...");
            var result = await api.GetAsync($"/api/sync?since={since}");
            if (result == null) return;

            var root        = result.RootElement;
            var cursor      = root.TryGetProperty("cursor",        out var c)  ? c.GetInt64()  : 0;
            var serverVer   = root.TryGetProperty("serverVersion", out var sv) ? sv.GetInt64() : 0;
            var items       = root.TryGetProperty("items",         out var i)  ? i.GetArrayLength() : 0;

            Console.WriteLine();
            Console.WriteLine("Sync Pull Summary");
            Console.WriteLine("─────────────────");
            Console.WriteLine($"Items returned:   {items}");
            Console.WriteLine($"New cursor:       {cursor}");
            Console.WriteLine($"Server version:   {serverVer}");

            if (items > 0)
            {
                // Tally by type
                var byType = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in root.GetProperty("items").EnumerateArray())
                {
                    var t = item.TryGetProperty("itemType", out var tp) ? tp.GetString() ?? "unknown" : "unknown";
                    byType[t] = byType.GetValueOrDefault(t) + 1;
                }
                Console.WriteLine();
                Console.WriteLine("By type:");
                foreach (var (type, count) in byType)
                    Console.WriteLine($"  {type,-24} {count}");
            }
        }, sinceOption);
    }
}
