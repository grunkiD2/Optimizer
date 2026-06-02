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
    }
}
