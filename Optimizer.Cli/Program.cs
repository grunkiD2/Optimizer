using System.CommandLine;
using Optimizer.Cli;

var rootCommand = new RootCommand("Optimizer CLI — control your system from the terminal")
{
    // Local desktop commands (talk to Optimizer.WinUI via OPTIMIZER_TOKEN)
    new StatusCommand(),
    new ApplyCommand(),
    new BatchCommand(),
    new ScanCommand(),
    new CleanupCommand(),
    new ProfileCommand(),
    new ScheduleCommand(),
};

return await rootCommand.InvokeAsync(args);
