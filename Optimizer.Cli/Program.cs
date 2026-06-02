using System.CommandLine;
using Optimizer.Cli;
using Optimizer.Cli.Commands.Cloud;

var rootCommand = new RootCommand("Optimizer CLI — control your system from the terminal")
{
    // Local desktop commands (talk to Optimizer.WinUI via OPTIMIZER_TOKEN)
    new StatusCommand(),
    new ApplyCommand(),
    new ScanCommand(),
    new CleanupCommand(),
    new ProfileCommand(),

    // Cloud server commands (talk to Optimizer.Server via OPTIMIZER_API_KEY)
    new CloudCommand(),
};

return await rootCommand.InvokeAsync(args);
