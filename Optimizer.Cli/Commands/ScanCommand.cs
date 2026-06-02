using System.CommandLine;

namespace Optimizer.Cli;

public class ScanCommand : Command
{
    public ScanCommand() : base("scan", "Run system diagnostics scan")
    {
        this.SetHandler(async () =>
        {
            var api = ApiClient.FromEnv();
            Console.WriteLine("Running diagnostics...");
            var result = await api.GetAsync("/api/recommendations");
            if (result == null) return;

            var arr = result.RootElement;
            int critical = 0, warning = 0, info = 0;
            foreach (var rec in arr.EnumerateArray())
            {
                var sev = rec.GetProperty("severity").GetString();
                switch (sev?.ToLower())
                {
                    case "critical": critical++; break;
                    case "warning":  warning++;  break;
                    default:         info++;     break;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Critical: {critical}");
            Console.WriteLine($"Warning:  {warning}");
            Console.WriteLine($"Info:     {info}");
            Console.WriteLine();

            foreach (var rec in arr.EnumerateArray())
            {
                var sev   = rec.GetProperty("severity").GetString();
                var title = rec.GetProperty("title").GetString();
                var icon  = sev switch
                {
                    "Critical" => "!",
                    "Warning"  => "~",
                    _          => "i"
                };
                Console.WriteLine($"  [{icon}] [{sev,-8}] {title}");
            }
        });
    }
}
