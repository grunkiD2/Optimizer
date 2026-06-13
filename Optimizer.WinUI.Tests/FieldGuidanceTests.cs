// FieldGuidanceTests.cs
using System.Linq;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FieldGuidanceTests
{
    [Theory]
    [InlineData("mode")]
    [InlineData("bright")]
    [InlineData("hdr")]
    [InlineData("lyd")]
    [InlineData("power")]
    public void Lookup_returns_guidance_for_every_known_field(string field)
    {
        var g = FieldGuidance.For(field);
        Assert.NotNull(g);
        Assert.False(string.IsNullOrWhiteSpace(g!.Hint));
        Assert.False(string.IsNullOrWhiteSpace(g.Tradeoff)); // den modsatrettede omkostning er ALTID med (spec §2)
    }

    [Fact]
    public void Lookup_unknown_field_returns_null() => Assert.Null(FieldGuidance.For("nope"));

    [Fact]
    public void EvidenceLine_carries_value_source_and_tier()
    {
        var line = new EvidenceLine("GPU p95", "59 °C", "lært på denne maskine (749 samples)", ConfidenceTier.Measured);
        Assert.Equal(ConfidenceTier.Measured, line.Tier);
        Assert.Equal("✓ målt", line.Tier.Badge());
        Assert.Equal("~ ekstern", ConfidenceTier.External.Badge());
        Assert.Equal("· afledt", ConfidenceTier.Derived.Badge());
    }

    [Fact]
    public void Picture_groups_are_ordered_and_findable()
    {
        var pic = new IntelligencePicture("destiny2.exe", "AAA-HDR", new[]
        {
            new IntelGroup("Ydelse (målt)", new[]
            {
                new EvidenceLine("FPS (gns.)", "130", "PresentMon, sidste session", ConfidenceTier.Measured),
            }),
        }, MaturityHave: 1, MaturityTarget: 3);
        Assert.Equal("destiny2.exe", pic.AppName);
        Assert.Single(pic.Groups);
        Assert.Equal("Ydelse (målt)", pic.Groups[0].Title);
        Assert.Equal(1, pic.MaturityHave);
    }
}
