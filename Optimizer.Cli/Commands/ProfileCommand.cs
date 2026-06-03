using System.CommandLine;

namespace Optimizer.Cli;

public class ProfileCommand : Command
{
    public ProfileCommand() : base("profile", "Manage profiles")
    {
        var listCommand = new Command("list", "List available profiles");
        listCommand.SetHandler(async () =>
        {
            var api = ApiClient.FromEnv();
            var result = await api.GetAsync("/api/profiles");
            if (result == null) return;

            Console.WriteLine("Available Profiles");
            Console.WriteLine("──────────────────");
            foreach (var p in result.RootElement.EnumerateArray())
            {
                var id   = p.GetProperty("id").GetString();
                var name = p.GetProperty("name").GetString();
                var desc = p.TryGetProperty("description", out var d) ? d.GetString() : "";
                Console.WriteLine($"  {id,-32} {name}");
                if (!string.IsNullOrEmpty(desc))
                    Console.WriteLine($"    {desc}");
            }
        });

        AddCommand(listCommand);

        // ── export ──────────────────────────────────────────────────────────
        var fileArg = new Argument<string?>("file", () => null, "Output file (default: stdout)");
        var exportCommand = new Command("export", "Export all saved profiles as JSON") { fileArg };
        exportCommand.SetHandler(async (string? file) =>
        {
            var api = ApiClient.FromEnv();
            var json = await api.GetStringAsync("/api/profiles/export");
            if (json == null) return;

            if (string.IsNullOrEmpty(file))
            {
                Console.WriteLine(json);
            }
            else
            {
                await File.WriteAllTextAsync(file, json);
                Console.WriteLine($"Exported profiles to {file}");
            }
        }, fileArg);
        AddCommand(exportCommand);

        // ── import ──────────────────────────────────────────────────────────
        var importFileArg = new Argument<string>("file", "JSON file to import");
        var importCommand = new Command("import", "Import profiles from a JSON file") { importFileArg };
        importCommand.SetHandler(async (string file) =>
        {
            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"File not found: {file}");
                Environment.Exit(1);
                return;
            }
            var json = await File.ReadAllTextAsync(file);
            var api = ApiClient.FromEnv();
            var result = await api.PostJsonAsync("/api/profiles/import", json);
            Console.WriteLine(result != null ? "Profiles imported." : "Import failed.");
        }, importFileArg);
        AddCommand(importCommand);
    }
}
