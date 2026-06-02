using System.CommandLine;

namespace Optimizer.Cli;

public class CleanupCommand : Command
{
    public CleanupCommand() : base("cleanup", "Clear temporary files")
    {
        this.SetHandler(async () =>
        {
            var api = ApiClient.FromEnv();
            Console.WriteLine("Running cleanup...");
            var result = await api.PostAsync("/api/cleanup");
            Console.WriteLine(result != null ? "Cleanup complete" : "Cleanup failed");
        });
    }
}
