using System.CommandLine;

namespace Optimizer.Cli;

public class ApplyCommand : Command
{
    public ApplyCommand() : base("apply", "Apply an optimization profile")
    {
        var profileArg = new Argument<string>("profile", "Profile ID (e.g. preset-gaming)");
        AddArgument(profileArg);

        this.SetHandler(async (string profile) =>
        {
            var api = ApiClient.FromEnv();
            Console.WriteLine($"Applying profile '{profile}'...");
            var result = await api.PostAsync($"/api/apply/{profile}");
            if (result == null) return;

            var r = result.RootElement;
            var success = r.GetProperty("success").GetBoolean();
            var message = r.TryGetProperty("message", out var m) ? m.GetString() : "";

            Console.WriteLine(success ? "Applied successfully" : $"Failed: {message}");
            Environment.Exit(success ? 0 : 1);
        }, profileArg);
    }
}
