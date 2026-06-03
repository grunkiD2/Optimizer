using System.CommandLine;

namespace Optimizer.Cli;

/// <summary>Manage scheduled profile/optimization runs on the local desktop API.</summary>
public class ScheduleCommand : Command
{
    public ScheduleCommand() : base("schedule", "Manage scheduled optimizations")
    {
        AddCommand(BuildList());
        AddCommand(BuildAdd());
        AddCommand(BuildRemove());
    }

    private static Command BuildList()
    {
        var list = new Command("list", "List scheduled tasks");
        list.SetHandler(async () =>
        {
            var api = ApiClient.FromEnv();
            var result = await api.GetAsync("/api/schedules");
            if (result == null) return;

            Console.WriteLine("Scheduled Tasks");
            Console.WriteLine("───────────────");
            foreach (var t in result.RootElement.EnumerateArray())
            {
                var id = t.GetProperty("id").GetString();
                var kind = t.GetProperty("kind").GetString();
                var target = t.GetProperty("targetId").GetString();
                var type = t.GetProperty("scheduleType").GetString();
                var value = t.GetProperty("scheduleValue").GetString();
                var enabled = t.GetProperty("enabled").GetBoolean();
                var next = t.TryGetProperty("nextRunUtc", out var n) ? n.GetString() : "";
                Console.WriteLine($"  [{(enabled ? "on " : "off")}] {kind} {target}  ({type} {value})");
                Console.WriteLine($"        id={id}  next={next}");
            }
        });
        return list;
    }

    private static Command BuildAdd()
    {
        var kindOpt = new Option<string>("--kind", () => "profile", "profile | optimization");
        var targetOpt = new Option<string>("--target", "Profile id or optimization id") { IsRequired = true };
        var typeOpt = new Option<string>("--type", () => "DailyAt", "DailyAt | IntervalMinutes | Once");
        var valueOpt = new Option<string>("--value", () => "03:00", "HH:mm | minutes | ISO-8601");

        var add = new Command("add", "Add a scheduled task") { kindOpt, targetOpt, typeOpt, valueOpt };
        add.SetHandler(async (string kind, string target, string type, string value) =>
        {
            var api = ApiClient.FromEnv();
            var body = new { kind, targetId = target, scheduleType = type, scheduleValue = value, enabled = true };
            var result = await api.PostAsync("/api/schedules", body);
            Console.WriteLine(result != null ? "Schedule created." : "Failed to create schedule.");
        }, kindOpt, targetOpt, typeOpt, valueOpt);
        return add;
    }

    private static Command BuildRemove()
    {
        var idArg = new Argument<string>("id", "Schedule id to remove");
        var remove = new Command("remove", "Remove a scheduled task") { idArg };
        remove.SetHandler(async (string id) =>
        {
            var api = ApiClient.FromEnv();
            var result = await api.DeleteAsync($"/api/schedules/{id}");
            Console.WriteLine(result ? "Schedule removed." : "Remove failed.");
        }, idArg);
        return remove;
    }
}
