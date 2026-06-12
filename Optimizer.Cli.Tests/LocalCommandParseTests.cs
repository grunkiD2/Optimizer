using System.CommandLine;
using System.CommandLine.Parsing;
using Optimizer.Cli;
using Xunit;

namespace Optimizer.Cli.Tests;

/// <summary>
/// Parse-level tests for the local desktop commands — validates command registration
/// and option handling without needing a live API or environment variables.
/// </summary>
public class LocalCommandParseTests
{
    private static RootCommand BuildRoot() => new("Optimizer CLI test")
    {
        new StatusCommand(),
        new ApplyCommand(),
        new BatchCommand(),
        new ScanCommand(),
        new CleanupCommand(),
        new ProfileCommand(),
        new ScheduleCommand(),
    };

    [Fact]
    public void Status_ParsesWithNoArguments()
    {
        var result = BuildRoot().Parse("status");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Apply_ParsesWithProfileArgument()
    {
        var result = BuildRoot().Parse("apply preset-gaming");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Apply_MissingProfileArgument_IsError()
    {
        var result = BuildRoot().Parse("apply");
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Scan_ParsesWithNoArguments()
    {
        var result = BuildRoot().Parse("scan");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Cleanup_ParsesWithNoArguments()
    {
        var result = BuildRoot().Parse("cleanup");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ScheduleList_Parses()
    {
        var result = BuildRoot().Parse("schedule list");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ScheduleAdd_ParsesWithOptions()
    {
        var result = BuildRoot().Parse("schedule add --target preset-gaming --type DailyAt --value 03:00");
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Schedule_HasExpectedSubcommands()
    {
        var schedule = new ScheduleCommand();
        var names = schedule.Subcommands.Select(c => c.Name).ToHashSet();

        Assert.Contains("list",   names);
        Assert.Contains("add",    names);
        Assert.Contains("remove", names);
    }

    [Fact]
    public void Root_HasExpectedLocalCommands()
    {
        var names = BuildRoot().Subcommands.Select(c => c.Name).ToHashSet();

        Assert.Contains("status",   names);
        Assert.Contains("apply",    names);
        Assert.Contains("batch",    names);
        Assert.Contains("scan",     names);
        Assert.Contains("cleanup",  names);
        Assert.Contains("profile",  names);
        Assert.Contains("schedule", names);

        // The cloud command group was deleted with the SaaS remnant (docs/VISION.md:
        // single-user, local-only) — it must not quietly come back.
        Assert.DoesNotContain("cloud", names);
    }

    [Fact]
    public void UnknownCommand_IsError()
    {
        var result = BuildRoot().Parse("cloud status");
        Assert.NotEmpty(result.Errors);
    }
}
