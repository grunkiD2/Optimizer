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

    // ── Profil 2.0 CRUD bridge ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateProfile_invokes_ctl_with_name_and_rejects_bad_tokens()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.True((await svc.CreateProfileAsync("Movie Night")).Success);
            Assert.Equal(["create-profile", "Movie Night"], calls.Single());
            calls.Clear();
            Assert.False((await svc.CreateProfileAsync("bad|name")).Success);  // pipe = separator
            Assert.False((await svc.CreateProfileAsync("  ")).Success);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CloneAndRename_join_with_pipe_separator()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.True((await svc.CloneProfileAsync("Desktop", "Desk2")).Success);
            Assert.Equal(["clone-profile", "Desktop|Desk2"], calls[^1]);
            Assert.True((await svc.RenameProfileAsync("Desk2", "Desk3")).Success);
            Assert.Equal(["rename-profile", "Desk2|Desk3"], calls[^1]);
            calls.Clear();
            Assert.False((await svc.CloneProfileAsync("Desktop", "bad|x")).Success);
            Assert.False((await svc.RenameProfileAsync("", "x")).Success);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task EditProfile_passes_raw_json_patch_and_rejects_invalid_json()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            var ok = await svc.EditProfileAsync("AAA-HDR", """{"display":{"bright":80},"optimizer":"preset-x"}""");
            Assert.True(ok.Success);
            Assert.Equal("edit-profile", calls.Single()[0]);
            Assert.Equal("""AAA-HDR|{"display":{"bright":80},"optimizer":"preset-x"}""", calls.Single()[1]);
            calls.Clear();
            Assert.False((await svc.EditProfileAsync("AAA-HDR", "not json")).Success);
            Assert.False((await svc.EditProfileAsync("AAA-HDR", "  ")).Success);
            Assert.False((await svc.EditProfileAsync("bad|name", """{"power":"x"}""")).Success);
            Assert.Empty(calls);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DeleteProfile_invokes_ctl_with_name()
    {
        var (svc, calls, root) = MakeService();
        try
        {
            Assert.True((await svc.DeleteProfileAsync("Competitive")).Success);
            Assert.Equal(["delete-profile", "Competitive"], calls.Single());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetProfiles_parses_full_v2_fields()
    {
        var root = Directory.CreateTempSubdirectory("fcroot").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            File.WriteAllText(Path.Combine(root, "profiles", "profiles.json"),
                """
                {"v":2,"profiles":{
                  "AAA-HDR":{"display":{"dc":8,"bright":100,"hdr":true},"power":"36531193-92c9-4772-911e-af2fa6f81bb0","lyd":"Kraken V4 Pro - Game","lys":{"mode":"static","color":"#8C00FF"},"optimizer":"preset-gaming","ui":{"icon":"G","desc":"AAA HDR"},"gamingClass":true},
                  "Night":{"display":{"dc":10,"bright":20,"hdr":false},"power":"a1841308-3541-4fab-bc81-f71556f20b4a","lyd":"","lys":{"mode":"off"},"optimizer":"","ui":{"icon":"","desc":""},"gamingClass":false}
                }}
                """);
            var svc = new FancontrolCommandService(Path.Combine(root, "state"),
                (args, ct) => Task.FromResult(new CtlResult(true, "ok")));
            var profiles = svc.GetProfiles();
            Assert.Equal(2, profiles.Count);
            var hdr = profiles.Single(p => p.Name == "AAA-HDR");
            Assert.Equal(8, hdr.Dc);
            Assert.Equal(100, hdr.Bright);
            Assert.True(hdr.Hdr);
            Assert.Equal("static", hdr.LysMode);
            Assert.Equal("#8C00FF", hdr.LysColor);
            Assert.Equal("preset-gaming", hdr.Optimizer);
            Assert.True(hdr.GamingClass);
            var night = profiles.Single(p => p.Name == "Night");
            Assert.Equal("off", night.LysMode);
            Assert.Null(night.LysColor);   // no color key on mode=off
            Assert.False(night.GamingClass);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void GetMappedPrograms_parses_learned_stats()
    {
        var root = Directory.CreateTempSubdirectory("fcroot").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            File.WriteAllText(Path.Combine(root, "profiles", "programs.json"),
                """
                [
                  {"exe":"destiny2","name":"Destiny 2","profile":"AAA-SDR","caseFloor":47,"radFloor":44,"learned":{"samples":749,"gpuP95":59.3,"gpuWavg":211}},
                  {"exe":"HD-Player","name":"BlueStacks","profile":"BlueStacks","caseFloor":44,"radFloor":40,"learned":null}
                ]
                """);
            var svc = new FancontrolCommandService(Path.Combine(root, "state"),
                (args, ct) => Task.FromResult(new CtlResult(true, "ok")));
            var progs = svc.GetMappedPrograms();
            Assert.Equal(2, progs.Count);
            var d2 = progs.Single(p => p.Exe == "destiny2");
            Assert.Equal("AAA-SDR", d2.Profile);
            Assert.Equal(47, d2.CaseFloor);
            Assert.Equal(59.3, d2.LearnedGpuP95);
            Assert.Equal(211, d2.LearnedGpuWatts);
            Assert.Equal(749, d2.LearnedSamples);
            var bs = progs.Single(p => p.Exe == "HD-Player");
            Assert.Null(bs.LearnedGpuP95);   // learned:null → no stats
            Assert.Equal(44, bs.CaseFloor);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void BuildProfilePatch_emits_lag2_only_and_omits_color_when_blank()
    {
        var withColor = FancontrolCommandService.BuildProfilePatch(5, 70, true,
            "36531193-92c9-4772-911e-af2fa6f81bb0", "Kraken", "static", "#8C00FF", "preset-gaming", "G", "AAA");
        using var d1 = System.Text.Json.JsonDocument.Parse(withColor);
        var r1 = d1.RootElement;
        Assert.Equal(5, r1.GetProperty("display").GetProperty("dc").GetInt32());
        Assert.True(r1.GetProperty("display").GetProperty("hdr").GetBoolean());
        Assert.Equal("#8C00FF", r1.GetProperty("lys").GetProperty("color").GetString());
        Assert.Equal("preset-gaming", r1.GetProperty("optimizer").GetString());
        Assert.False(r1.TryGetProperty("gamingClass", out _));  // lag-1 never emitted

        var noColor = FancontrolCommandService.BuildProfilePatch(10, 20, false,
            "a1841308-3541-4fab-bc81-f71556f20b4a", "", "off", "", "", "", "");
        using var d2 = System.Text.Json.JsonDocument.Parse(noColor);
        Assert.Equal("off", d2.RootElement.GetProperty("lys").GetProperty("mode").GetString());
        Assert.False(d2.RootElement.GetProperty("lys").TryGetProperty("color", out _));  // colorless mode stays colorless
    }

    // ── R1 result contract: last stdout line is JSON {ok,cmd,msg} + real exit code ──

    [Fact]
    public void ParseCtlResult_trusts_ok_true_with_zero_exit()
    {
        var stdout = "kommando 'night on' afleveret - venter paa hjernens opsamling (<=12 s)\r\n" +
                     "udfoert af hjernen efter 2.4 s\r\n" +
                     """{"ok":true,"cmd":"night","msg":"'night on' udfoert af hjernen efter 2.4 s"}""";
        var r = FancontrolCommandService.ParseCtlResult(stdout, "", 0);
        Assert.True(r.Success);
        Assert.Equal("'night on' udfoert af hjernen efter 2.4 s", r.Output);
    }

    [Fact]
    public void ParseCtlResult_ok_false_fails_even_with_zero_exit()
    {
        var stdout = "afvist: 'BogusTask' er ikke i whitelisten\r\n" +
                     """{"ok":false,"cmd":"run-task","msg":"afvist: 'BogusTask' er ikke i whitelisten"}""";
        var r = FancontrolCommandService.ParseCtlResult(stdout, "", 0);
        Assert.False(r.Success);
        Assert.Contains("afvist", r.Output);
    }

    [Fact]
    public void ParseCtlResult_ok_true_with_nonzero_exit_fails_closed()
    {
        var r = FancontrolCommandService.ParseCtlResult("""{"ok":true,"cmd":"reload","msg":"x"}""", "", 1);
        Assert.False(r.Success);
    }

    [Fact]
    public void ParseCtlResult_missing_contract_fails_closed()
    {
        // Pre-R1 engine (or a crash before the JSON line): prose only, exit 0 — must NOT count as success.
        var r = FancontrolCommandService.ParseCtlResult("kommando 'night on' afleveret (hjernen samler op inden for 5 s)", "", 0);
        Assert.False(r.Success);
        Assert.Contains("no R1 JSON result contract", r.Output);
    }

    [Fact]
    public void ParseCtlResult_garbled_json_fails_closed()
    {
        var r = FancontrolCommandService.ParseCtlResult("{not valid json", "", 0);
        Assert.False(r.Success);
    }

    [Fact]
    public void ParseCtlResult_handles_trailing_crlf_and_unicode_escapes()
    {
        // The real powershell.exe form: CRLF line endings INCLUDING a trailing one, and the
        // engine \uXXXX-escapes all non-ASCII (verbatim live capture, 2026-06-13).
        var stdout = "kommando 'night on' afleveret - venter paa hjernens opsamling (<=15 s)\r\n" +
                     "udfoert af hjernen efter 2.4 s\r\n" +
                     "{\"ok\":true,\"cmd\":\"night\",\"msg\":\"\\u0027night on\\u0027 udfoert af hjernen efter 2.4 s\"}\r\n";
        var r = FancontrolCommandService.ParseCtlResult(stdout, "", 0);
        Assert.True(r.Success);
        Assert.Equal("'night on' udfoert af hjernen efter 2.4 s", r.Output);
    }

    [Fact]
    public void ParseCtlResult_ok_false_with_exit_one_is_the_common_failure_form()
    {
        var stdout = "Unknown profile: Bogus\r\n" +
                     "{\"ok\":false,\"cmd\":\"apply-profile\",\"msg\":\"ukendt profil: \\u0027Bogus\\u0027\"}\r\n";
        var r = FancontrolCommandService.ParseCtlResult(stdout, "", 1);
        Assert.False(r.Success);
        Assert.Equal("ukendt profil: 'Bogus'", r.Output);
    }

    [Fact]
    public void ParseCtlResult_json_without_ok_property_fails_closed()
    {
        // A state-file dump as last line (e.g. someone pipes `status` oddly) must not be mistaken for the contract.
        var r = FancontrolCommandService.ParseCtlResult("""{"mode":"NIGHT","night":true}""", "", 0);
        Assert.False(r.Success);
    }

    [Fact]
    public void ParseCtlResult_stderr_noise_does_not_break_stdout_contract()
    {
        var r = FancontrolCommandService.ParseCtlResult(
            """{"ok":true,"cmd":"ack-alerts","msg":"kvitteret: 2 alert(s)"}""",
            "Some PowerShell warning on stderr", 0);
        Assert.True(r.Success);
        Assert.Equal("kvitteret: 2 alert(s)", r.Output);
    }

    [Fact]
    public void ParseCtlResult_empty_msg_falls_back_to_raw_output()
    {
        var r = FancontrolCommandService.ParseCtlResult("prosa-linje\r\n" + """{"ok":true,"cmd":"x","msg":""}""", "", 0);
        Assert.True(r.Success);
        Assert.Contains("prosa-linje", r.Output);
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

    // ── Profil 2.0 Task 4: hdrType round-trip ────────────────────────────────

    [Fact]
    public void BuildProfilePatch_includes_hdrType_when_set()
    {
        var json = FancontrolCommandService.BuildProfilePatch(7, 80, true, "381b4222-0000-0000-0000-000000000000",
            "", "synapse", null, "", "", "", hdrType: "console");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var display = doc.RootElement.GetProperty("display");
        Assert.Equal("console", display.GetProperty("hdrType").GetString());
    }

    [Fact]
    public void BuildProfilePatch_omits_hdrType_when_blank()
    {
        var json = FancontrolCommandService.BuildProfilePatch(7, 80, false, "381b4222-0000-0000-0000-000000000000",
            "", "synapse", null, "", "", "", hdrType: "");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("display").TryGetProperty("hdrType", out _));
    }

    [Fact]
    public void GetProfiles_parses_hdrType_from_display()
    {
        var root = Directory.CreateTempSubdirectory("fcroot").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles"));
            File.WriteAllText(Path.Combine(root, "profiles", "profiles.json"),
                """{"profiles":{"D2":{"display":{"dc":150,"bright":50,"hdr":true,"hdrType":"console"}}}}""");
            var svc = new FancontrolCommandService(Path.Combine(root, "state"), (a, c) => Task.FromResult(new CtlResult(true, "ok")));
            var d2 = svc.GetProfiles().Single(p => p.Name == "D2");
            Assert.Equal(150, d2.Dc);
            Assert.Equal("console", d2.HdrType);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
