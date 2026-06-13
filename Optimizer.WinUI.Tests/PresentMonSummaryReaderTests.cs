// PresentMonSummaryReaderTests.cs
using System;
using System.IO;
using System.Linq;
using Optimizer.WinUI.Services.Intelligence;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class PresentMonSummaryReaderTests
{
    [Fact]
    public void LatestForApp_returns_newest_summary_by_capture_end()
    {
        var root = Directory.CreateTempSubdirectory("pm").FullName;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "presentmon")).FullName;
            File.WriteAllText(Path.Combine(dir, "summary-20260612-1201.json"),
                """{"app":"destiny2.exe","fpsAvg":130.0,"fps1Low":58.0,"ftP95":11.8,"frames":500000,"start":"2026-06-12T12:01:00+02:00","end":"2026-06-12T13:09:00+02:00"}""");
            File.WriteAllText(Path.Combine(dir, "summary-20260613-2000.json"),
                """{"app":"destiny2.exe","fpsAvg":196.6,"fps1Low":85.7,"ftP95":8.4,"frames":300000,"start":"2026-06-13T20:00:00+02:00","end":"2026-06-13T20:25:00+02:00"}""");
            File.WriteAllText(Path.Combine(dir, "summary-20260613-2100.json"),
                """{"app":"other.exe","fpsAvg":60.0,"fps1Low":40.0,"ftP95":20.0,"frames":120000,"start":"2026-06-13T21:00:00+02:00","end":"2026-06-13T21:30:00+02:00"}""");

            var reader = new PresentMonSummaryReader(root);
            var s = reader.LatestForApp("destiny2.exe");
            Assert.NotNull(s);
            Assert.Equal(196.6, s!.FpsAvg, 1);     // nyeste (2026-06-13) vinder
            Assert.Equal(85.7, s.Fps1Low, 1);
            Assert.Equal("destiny2.exe", s.App);
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void LatestForApp_is_case_insensitive_on_exe() // brain/programs.json matcher case-insensitivt
    {
        var root = Directory.CreateTempSubdirectory("pm").FullName;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "presentmon")).FullName;
            File.WriteAllText(Path.Combine(dir, "summary-1.json"),
                """{"app":"Destiny2.exe","fpsAvg":120.0,"fps1Low":50.0,"ftP95":12.0,"frames":200000,"start":"2026-06-12T12:00:00+02:00","end":"2026-06-12T12:30:00+02:00"}""");
            var reader = new PresentMonSummaryReader(root);
            Assert.NotNull(reader.LatestForApp("destiny2.exe"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Count_for_app_supports_maturity_indicator()
    {
        var root = Directory.CreateTempSubdirectory("pm").FullName;
        try
        {
            var dir = Directory.CreateDirectory(Path.Combine(root, "presentmon")).FullName;
            for (int i = 0; i < 2; i++)
                File.WriteAllText(Path.Combine(dir, $"summary-{i}.json"),
                    $$"""{"app":"destiny2.exe","fpsAvg":120.0,"fps1Low":50.0,"ftP95":12.0,"frames":200000,"start":"2026-06-12T12:0{{i}}:00+02:00","end":"2026-06-12T12:3{{i}}:00+02:00"}""");
            Assert.Equal(2, new PresentMonSummaryReader(root).CountForApp("destiny2.exe"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void Missing_dir_returns_null_and_zero_gracefully()
    {
        var reader = new PresentMonSummaryReader(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));
        Assert.Null(reader.LatestForApp("any.exe"));
        Assert.Equal(0, reader.CountForApp("any.exe"));
    }
}
