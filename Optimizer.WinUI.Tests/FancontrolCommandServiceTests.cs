using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FancontrolCommandServiceTests
{
    private static (FancontrolCommandService svc, List<IReadOnlyList<string>> calls, string root) MakeService()
    {
        var root = Directory.CreateTempSubdirectory("fcroot").FullName;
        Directory.CreateDirectory(Path.Combine(root, "profiles"));
        File.WriteAllText(Path.Combine(root, "profiles", "profiles.json"),
            """{"profiles":{"Desktop":{},"Night":{},"Competitive":{},"AAA-HDR":{}}}""");
        var calls = new List<IReadOnlyList<string>>();
        var svc = new FancontrolCommandService(Path.Combine(root, "state"),
            (args, ct) => { calls.Add(args); return Task.FromResult(new CtlResult(true, "ok")); });
        return (svc, calls, root);
    }

    [Fact]
    public async Task ApplyProfile_accepts_known_names_including_module_suffix()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.True((await svc.ApplyProfileAsync("AAA-HDR")).Success);
            Assert.True((await svc.ApplyProfileAsync("Night+lyd")).Success);
            Assert.Equal(2, calls.Count);
            Assert.Equal(["apply-profile", "AAA-HDR"], calls[0]);
            Assert.Equal(["apply-profile", "Night+lyd"], calls[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ApplyProfile_rejects_unknown_names_without_invoking_ctl()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            var r = await svc.ApplyProfileAsync("EvilProfile; Remove-Item -Recurse");
            Assert.False(r.Success);
            Assert.Contains("Unknown profile", r.Output);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ApplyProfile_fails_closed_when_profiles_json_is_missing()
    {
        var root = Directory.CreateTempSubdirectory("fcroot").FullName;
        try
        {
            var calls = new List<IReadOnlyList<string>>();
            var svc = new FancontrolCommandService(Path.Combine(root, "state"),
                (args, ct) => { calls.Add(args); return Task.FromResult(new CtlResult(true, "ok")); });
            var r = await svc.ApplyProfileAsync("Desktop");
            Assert.False(r.Success);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("on")]
    [InlineData("OFF")]
    [InlineData("Auto")]
    public async Task Night_accepts_whitelisted_modes_case_insensitively(string mode)
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.True((await svc.SetNightAsync(mode)).Success);
            Assert.Equal("night", calls.Single()[0]);
            Assert.Equal(mode.ToLowerInvariant(), calls.Single()[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Night_rejects_anything_else()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.False((await svc.SetNightAsync("maybe; shutdown")).Success);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task AckAlerts_strips_control_chars_and_caps_note_length()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            await svc.AckAlertsAsync("line1\r\nline2\t" + new string('x', 300));
            var note = calls.Single()[1];
            Assert.DoesNotContain('\n', note);
            Assert.DoesNotContain('\r', note);
            Assert.True(note.Length <= 200);
            Assert.StartsWith("line1line2", note);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Unconfigured_service_refuses_everything()
    {
        var svc = new FancontrolCommandService("");
        Assert.False(svc.IsConfigured);
        Assert.False((await svc.SetNightAsync("on")).Success);
        Assert.False((await svc.AckAlertsAsync(null)).Success);
    }

    // ── Token hardening ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Bearer secret-token", "secret-token", true)]
    [InlineData("Bearer wrong-token!!", "secret-token", false)]   // same length, wrong bytes
    [InlineData("Bearer secret-toke", "secret-token", false)]     // shorter
    [InlineData("Bearer secret-tokens", "secret-token", false)]   // longer
    [InlineData("bearer secret-token", "secret-token", false)]    // wrong prefix case
    [InlineData("secret-token", "secret-token", false)]           // missing prefix
    [InlineData("", "secret-token", false)]
    public void TokenMatches_is_exact_and_prefix_strict(string header, string token, bool expected)
        => Assert.Equal(expected, ApiHostService.TokenMatches(header, token));

    [Fact]
    public void TokenMatches_rejects_empty_configured_token()
        => Assert.False(ApiHostService.TokenMatches("Bearer ", ""));
}
