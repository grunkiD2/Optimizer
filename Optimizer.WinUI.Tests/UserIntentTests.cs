using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class UserIntentTests
{
    [Fact]
    public void None_has_all_bits_false()
    {
        var n = UserIntent.None;
        Assert.False(n.Gaming);
        Assert.False(n.Family);
        Assert.False(n.Creativity);
        Assert.False(n.Schoolwork);
        Assert.False(n.Entertainment);
        Assert.False(n.Business);
        Assert.False(n.Development);
        Assert.False(n.DevModeEnabled);
    }

    [Fact]
    public void None_prompt_hint_says_none_declared()
    {
        Assert.Equal("none declared", UserIntent.None.ToPromptHint());
    }

    [Fact]
    public void Single_bit_renders_alone()
    {
        var only_gaming = new UserIntent(true, false, false, false, false, false, false, false);
        Assert.Equal("Gaming", only_gaming.ToPromptHint());
    }

    [Fact]
    public void Multiple_bits_render_comma_separated_in_canonical_order()
    {
        var multi = new UserIntent(
            Gaming: true,
            Family: false,
            Creativity: true,
            Schoolwork: false,
            Entertainment: false,
            Business: true,
            Development: false,
            DevModeEnabled: true);

        // Order is fixed: the rendering walks bits in declaration order.
        Assert.Equal("Gaming, Creativity, Business, DevMode", multi.ToPromptHint());
    }

    [Fact]
    public void DevMode_appears_independently_of_Development_bit()
    {
        var dev_mode_only = new UserIntent(
            Gaming: false, Family: false, Creativity: false, Schoolwork: false,
            Entertainment: false, Business: false, Development: false, DevModeEnabled: true);
        Assert.Equal("DevMode", dev_mode_only.ToPromptHint());

        var both = new UserIntent(
            Gaming: false, Family: false, Creativity: false, Schoolwork: false,
            Entertainment: false, Business: false, Development: true, DevModeEnabled: true);
        Assert.Equal("Development, DevMode", both.ToPromptHint());
    }
}
