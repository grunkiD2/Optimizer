using System.CommandLine;

namespace Optimizer.Cli;

/// <summary>Apply several profiles/optimizations in one request via POST /api/apply/batch.</summary>
public class BatchCommand : Command
{
    public BatchCommand() : base("batch", "Apply multiple profiles/optimizations at once")
    {
        var profilesOpt = new Option<string[]>("--profiles", "Profile ids to apply")
        { AllowMultipleArgumentsPerToken = true };
        var optsOpt = new Option<string[]>("--optimizations", "Optimization ids to apply")
        { AllowMultipleArgumentsPerToken = true };

        AddOption(profilesOpt);
        AddOption(optsOpt);

        this.SetHandler(async (string[] profiles, string[] optimizations) =>
        {
            var items = new List<object>();
            foreach (var p in profiles ?? Array.Empty<string>())
                items.Add(new { type = "profile", id = p });
            foreach (var o in optimizations ?? Array.Empty<string>())
                items.Add(new { type = "optimization", id = o });

            if (items.Count == 0)
            {
                Console.Error.WriteLine("Specify at least one --profiles or --optimizations value.");
                Environment.Exit(1);
                return;
            }

            var api = ApiClient.FromEnv();
            var result = await api.PostAsync("/api/apply/batch", items);
            if (result == null) return;

            Console.WriteLine("Batch Apply Results");
            Console.WriteLine("───────────────────");
            var anyFailed = false;
            foreach (var item in result.RootElement.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var success = item.GetProperty("success").GetBoolean();
                var reason = item.TryGetProperty("reason", out var r) ? r.GetString() : "";
                if (!success) anyFailed = true;
                Console.WriteLine($"  {(success ? "✓" : "✗")} {id,-32} {reason}");
            }
            Environment.Exit(anyFailed ? 1 : 0);
        }, profilesOpt, optsOpt);
    }
}
