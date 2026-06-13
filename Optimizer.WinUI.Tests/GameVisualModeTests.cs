using System.Linq;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class GameVisualModeTests
{
    [Theory]
    [InlineData(7, "FPS")]
    [InlineData(5, "Racing")]
    [InlineData(1, "Cinema")]
    [InlineData(10, "sRGB")]
    public void NameForDc_returns_known_mode(int dc, string expected)
        => Assert.Equal(expected, GameVisualMode.NameForDc(dc));

    [Theory]
    [InlineData(150)]
    [InlineData(8)]
    [InlineData(255)]
    public void NameForDc_returns_null_for_unknown(int dc)
        => Assert.Null(GameVisualMode.NameForDc(dc));

    [Fact]
    public void Label_names_known_and_marks_unknown()
    {
        Assert.Equal("FPS", GameVisualMode.Label(7));
        Assert.Equal("Brugerdefineret (150)", GameVisualMode.Label(150));
    }

    [Theory]
    [InlineData("FPS", 7)]
    [InlineData("sRGB", 10)]
    public void DcForName_roundtrips(string name, int dc)
        => Assert.Equal(dc, GameVisualMode.DcForName(name));

    [Fact]
    public void DcForName_unknown_name_returns_null() => Assert.Null(GameVisualMode.DcForName("Nope"));

    [Fact]
    public void Named_lists_exactly_the_four_modes()
        => Assert.Equal(["FPS", "Racing", "Cinema", "sRGB"], GameVisualMode.NamedModes.Select(m => m.Name).ToArray());
}
