using System.CommandLine;
using System.CommandLine.Parsing;
using Optimizer.Cli.Commands.Cloud;
using Xunit;

namespace Optimizer.Cli.Tests;

/// <summary>
/// Parse-level tests for cloud subcommands — validates command registration
/// and option handling without needing a live server or environment variables.
/// </summary>
public class CloudCommandParseTests
{
    private static RootCommand BuildRoot()
    {
        var root = new RootCommand("Optimizer CLI test");
        root.AddCommand(new CloudCommand());
        return root;
    }

    [Fact]
    public void CloudStatus_ParsesWithNoArguments()
    {
        var root   = BuildRoot();
        var result = root.Parse("cloud status");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CloudMarketplace_ParsesWithOptions()
    {
        var root   = BuildRoot();
        var result = root.Parse("cloud marketplace --category Performance --search boost");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CloudPlugins_ParsesWithSearch()
    {
        var root   = BuildRoot();
        var result = root.Parse("cloud plugins --search privacy");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CloudSync_ParsesWithSinceOption()
    {
        var root   = BuildRoot();
        var result = root.Parse("cloud sync --since 42");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CloudKeys_ListSubcommandExists()
    {
        var root   = BuildRoot();
        var result = root.Parse("cloud keys list");

        Assert.Empty(result.Errors);
    }

    [Fact]
    public void CloudCommand_HasExpectedSubcommands()
    {
        var cloud = new CloudCommand();
        var names = cloud.Subcommands.Select(c => c.Name).ToHashSet();

        Assert.Contains("status",      names);
        Assert.Contains("sync",        names);
        Assert.Contains("marketplace", names);
        Assert.Contains("plugins",     names);
        Assert.Contains("keys",        names);
    }
}
