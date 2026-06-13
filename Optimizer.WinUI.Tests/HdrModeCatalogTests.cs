using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class HdrModeCatalogTests
{
    [Fact]
    public void Catalog_lists_hdr10_and_dolbyvision_groups()
    {
        Assert.Contains(HdrModeCatalog.All, m => m.Key == "trueblack" && m.Group == "HDR10");
        Assert.Contains(HdrModeCatalog.All, m => m.Key == "console" && m.Group == "HDR10");
        Assert.Contains(HdrModeCatalog.All, m => m.Key == "dv-game" && m.Group == "Dolby Vision");
    }

    [Fact]
    public void Recipe_for_console_names_the_osd_path()
    {
        var r = HdrModeCatalog.Recipe("console");
        Assert.Contains("HDR Format", r);
        Assert.Contains("Console", r);
    }

    [Fact]
    public void Recipe_unknown_key_is_empty() => Assert.Equal("", HdrModeCatalog.Recipe("nope"));

    [Fact]
    public void Label_returns_human_name() => Assert.Equal("DisplayHDR 400 True Black", HdrModeCatalog.Label("trueblack"));
}
