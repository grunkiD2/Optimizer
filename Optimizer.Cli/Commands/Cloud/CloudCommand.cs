using System.CommandLine;

namespace Optimizer.Cli.Commands.Cloud;

/// <summary>
/// Top-level "cloud" command group.
/// Subcommands interact with the Optimizer cloud server (Optimizer.Server)
/// using an API key (OPTIMIZER_API_KEY env var) — completely separate from
/// the local desktop API commands which use OPTIMIZER_TOKEN.
/// </summary>
public class CloudCommand : Command
{
    public CloudCommand() : base("cloud", "Interact with the Optimizer cloud server")
    {
        AddCommand(new CloudStatusCommand());
        AddCommand(new CloudSyncCommand());
        AddCommand(new CloudMarketplaceCommand());
        AddCommand(new CloudPluginsCommand());
        AddCommand(new CloudKeysCommand());
    }
}
