using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services.Commands;
using Optimizer.WinUI.Services.Assistant;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ToolCatalogTests
{
    private sealed class FakeCommand(string id, bool confirm) : IAppCommand
    {
        public string Id => id;
        public string Description => $"desc-{id}";
        public JsonElement ParametersSchema => SchemaJson.Empty;
        public bool IsReadOnly => !confirm;
        public bool RequiresConfirmation => confirm;
        public Task<CommandResult> ExecuteAsync(JsonElement args, CancellationToken ct) => Task.FromResult(CommandResult.Ok("x"));
    }

    [Fact]
    public void Build_maps_every_command_to_a_tool_def()
    {
        var reg = new CommandRegistry();
        reg.Register(new FakeCommand("get_metrics", confirm: false));
        reg.Register(new FakeCommand("apply_profile", confirm: true));

        var tools = ToolCatalog.Build(reg, allowActions: true);

        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "get_metrics" && t.Description == "desc-get_metrics");
        Assert.Contains(tools, t => t.Name == "apply_profile");
    }

    [Fact]
    public void Build_excludes_confirm_commands_when_actions_disabled()
    {
        var reg = new CommandRegistry();
        reg.Register(new FakeCommand("get_metrics", confirm: false));
        reg.Register(new FakeCommand("apply_profile", confirm: true));

        var tools = ToolCatalog.Build(reg, allowActions: false);

        Assert.Single(tools);
        Assert.Equal("get_metrics", tools[0].Name);
    }
}
