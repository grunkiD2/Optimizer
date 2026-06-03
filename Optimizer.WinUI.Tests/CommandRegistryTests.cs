using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class CommandRegistryTests
{
    private sealed class FakeCommand(string id, bool readOnly = true, bool confirm = false) : IAppCommand
    {
        public string Id => id;
        public string Description => $"desc-{id}";
        public JsonElement ParametersSchema => JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        public bool IsReadOnly => readOnly;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct)
            => Task.FromResult(CommandResult.Ok("ran"));
    }

    [Fact]
    public void Register_then_Find_returns_command()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("get_metrics"));
        Assert.NotNull(r.Find("get_metrics"));
        Assert.Single(r.Commands);
    }

    [Fact]
    public void Find_unknown_returns_null()
    {
        var r = new CommandRegistry();
        Assert.Null(r.Find("nope"));
    }

    [Fact]
    public void Register_duplicate_id_throws()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("apply_profile"));
        Assert.Throws<InvalidOperationException>(() => r.Register(new FakeCommand("apply_profile")));
    }

    [Fact]
    public void Metadata_is_preserved()
    {
        var r = new CommandRegistry();
        r.Register(new FakeCommand("apply_profile", readOnly: false, confirm: true));
        var c = r.Find("apply_profile")!;
        Assert.False(c.IsReadOnly);
        Assert.True(c.RequiresConfirmation);
    }
}
