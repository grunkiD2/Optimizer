using System.CommandLine;

namespace Optimizer.Cli.Commands.Cloud;

public class CloudKeysCommand : Command
{
    public CloudKeysCommand() : base("keys", "API key management")
    {
        var listCommand = new Command("list", "List your API keys");
        listCommand.SetHandler(() =>
        {
            // API key management requires JWT authentication (by design — you cannot
            // mint or list keys using a key itself, only a JWT session). The CLI uses
            // API-key auth, so this operation must be done interactively.
            Console.WriteLine("API key management requires an interactive JWT session.");
            Console.WriteLine();
            Console.WriteLine("To manage your API keys, use one of:");
            Console.WriteLine("  • Desktop app → Settings → Developer");
            Console.WriteLine("  • POST /api/keys  (with a JWT Bearer token from magic-link login)");
            Console.WriteLine();
            Console.WriteLine("The CLI authenticates via X-Api-Key header, which cannot be used");
            Console.WriteLine("to create or list other API keys (by design, to prevent privilege escalation).");
        });

        AddCommand(listCommand);
    }
}
