using System;
using System.IO;
using System.Linq;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FancontrolEventsServiceTests
{
    // Verbatim live lines (2026-06-13 capture).
    private const string BrainLine = """{"src":"brain","msg":"CMD night=auto","ts":"2026-06-13T00:43:46.1234567+02:00"}""";
    private const string CtlLine = """{"src":"ctl","msg":"result:run-task ok=False afvist: 'FixtureProbe' er ikke i whitelisten","ts":"2026-06-13T01:18:07.1553395+02:00"}""";

    [Fact]
    public void ParseLine_maps_the_real_contract()
    {
        var ev = FancontrolEventsService.ParseLine(CtlLine);
        Assert.NotNull(ev);
        Assert.Equal("ctl", ev!.Src);
        Assert.Contains("'FixtureProbe'", ev.Msg);   // ' decoded
        Assert.Equal(new DateTimeOffset(2026, 6, 13, 1, 18, 7, 155, TimeSpan.FromHours(2)),
            new DateTimeOffset(ev.Ts.Year, ev.Ts.Month, ev.Ts.Day, ev.Ts.Hour, ev.Ts.Minute, ev.Ts.Second, ev.Ts.Millisecond, ev.Ts.Offset));
    }

    [Fact]
    public void ParseLine_rejects_garbage_without_throwing()
    {
        Assert.Null(FancontrolEventsService.ParseLine("{not json"));
        Assert.Null(FancontrolEventsService.ParseLine("""{"msg":"no ts"}"""));
    }

    [Fact]
    public void ReadTail_returns_newest_first_and_caps_count()
    {
        var root = Directory.CreateTempSubdirectory("fcev").FullName;
        try
        {
            File.WriteAllLines(Path.Combine(root, "events.jsonl"),
            [
                BrainLine,
                "garbage line that must be skipped",
                CtlLine,
            ]);
            var svc = new FancontrolEventsService(root);
            var tail = svc.ReadTail(10);
            Assert.Equal(2, tail.Count);
            Assert.Equal("ctl", tail[0].Src);     // newest first
            Assert.Equal("brain", tail[1].Src);

            Assert.Single(svc.ReadTail(1));
            Assert.Equal("ctl", svc.ReadTail(1).Single().Src);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unconfigured_or_missing_file_returns_empty()
    {
        Assert.Empty(new FancontrolEventsService("").ReadTail(10));
        var root = Directory.CreateTempSubdirectory("fcev2").FullName;
        try { Assert.Empty(new FancontrolEventsService(root).ReadTail(10)); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
