using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AppCommandsMutatingTests
{
    [Fact]
    public void Mutating_commands_require_confirmation_and_are_not_readonly()
    {
        var opt = new Mock<IWindowsOptimizerService>().Object;
        IAppCommand[] cmds =
        [
            new ApplyProfileCommand(opt),
            new ApplyOptimizationCommand(opt),
            new RunCleanupCommand(opt),
            new UndoLastCommand(opt),
        ];
        Assert.All(cmds, c => Assert.True(c.RequiresConfirmation));
        Assert.All(cmds, c => Assert.False(c.IsReadOnly));
    }

    [Fact]
    public async Task ApplyProfile_routes_id_to_service()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.ApplyProfileAsync("preset-privacy")).ReturnsAsync(true);
        var cmd = new ApplyProfileCommand(opt.Object);
        var args = SchemaJson.Parse("""{"profile_id":"preset-privacy"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.True(result.Success);
        opt.Verify(o => o.ApplyProfileAsync("preset-privacy"), Times.Once);
    }

    [Fact]
    public async Task ApplyProfile_missing_id_fails_without_calling_service()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        var cmd = new ApplyProfileCommand(opt.Object);
        var result = await cmd.ExecuteAsync(SchemaJson.Empty, default);
        Assert.False(result.Success);
        opt.Verify(o => o.ApplyProfileAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UndoLast_with_no_entries_is_noop_ok()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.GetUndoEntries()).Returns(new List<UndoEntry>());
        var cmd = new UndoLastCommand(opt.Object);
        var result = await cmd.ExecuteAsync(SchemaJson.Empty, default);
        Assert.True(result.Success);
        Assert.Contains("Nothing to undo", result.Summary);
    }
}
