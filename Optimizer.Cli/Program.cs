using System.CommandLine;
using Optimizer.Cli;

var rootCommand = new RootCommand("Optimizer CLI — control your system from the terminal")
{
    new StatusCommand(),
    new ApplyCommand(),
    new ScanCommand(),
    new CleanupCommand(),
    new ProfileCommand(),
};

return await rootCommand.InvokeAsync(args);
