using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Commands;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class AppCommandsReadTests
{
    private static JsonElement NoArgs => SchemaJson.Empty;

    [Fact]
    public async Task ListProfiles_summarizes_presets()
    {
        var opt = new Mock<IWindowsOptimizerService>();
        opt.Setup(o => o.GetBuiltInPresets()).Returns(new List<SettingsProfile>
        {
            new() { Id = "preset-gaming", Name = "Gaming", Description = "Fast" }
        });
        var cmd = new ListProfilesCommand(opt.Object);
        var result = await cmd.ExecuteAsync(NoArgs, default);
        Assert.True(result.Success);
        Assert.Contains("preset-gaming", result.Summary);
        Assert.True(cmd.IsReadOnly);
        Assert.False(cmd.RequiresConfirmation);
    }

    [Fact]
    public async Task GetRecommendations_handles_empty()
    {
        var recs = new Mock<IRecommendationsService>();
        recs.Setup(r => r.GenerateAsync()).ReturnsAsync(new List<Recommendation>());
        var cmd = new GetRecommendationsCommand(recs.Object);
        var result = await cmd.ExecuteAsync(NoArgs, default);
        Assert.True(result.Success);
        Assert.Contains("No recommendations", result.Summary);
    }

    [Fact]
    public async Task NavigateToPage_unknown_page_fails_with_known_list()
    {
        var nav = new Mock<IPageNavigator>();
        nav.Setup(n => n.Pages).Returns(new[] { "Dashboard", "Diagnostics" });
        nav.Setup(n => n.NavigateTo("Banana")).Returns(false);
        var cmd = new NavigateToPageCommand(nav.Object);
        var args = SchemaJson.Parse("""{"page":"Banana"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.False(result.Success);
        Assert.Contains("Dashboard", result.Summary);
    }

    [Fact]
    public async Task NavigateToPage_known_page_succeeds()
    {
        var nav = new Mock<IPageNavigator>();
        nav.Setup(n => n.NavigateTo("Diagnostics")).Returns(true);
        var cmd = new NavigateToPageCommand(nav.Object);
        var args = SchemaJson.Parse("""{"page":"Diagnostics"}""");
        var result = await cmd.ExecuteAsync(args, default);
        Assert.True(result.Success);
    }
}
