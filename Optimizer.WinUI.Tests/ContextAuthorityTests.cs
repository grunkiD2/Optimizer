using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ContextAuthorityTests
{
    private sealed class FakeGuesser : IContextGuesser
    {
        public string Answer = "Unknown";
        public bool? LastSuppressGaming;
        public UserIntent UserIntent => UserIntent.None;
        public Task<string> GuessContextAsync(bool suppressGaming)
        {
            LastSuppressGaming = suppressGaming;
            // A suppressed call must never answer Gaming — mirror the real implementation.
            return Task.FromResult(suppressGaming && Answer == "Gaming" ? "Unknown" : Answer);
        }
    }

    private sealed class FakeStatus : IFancontrolStatusService
    {
        public bool Configured = true;
        public FancontrolStatus? Status;
        public bool IsConfigured => Configured;
        public FancontrolStatus? GetStatus() => Status;
    }

    private static FancontrolStatus FreshStatus(bool game, string? profile, bool brainStale = false, bool profilesStale = false, string? mode = null) => new()
    {
        Brain = new FancontrolBrainStatus { Game = game, Stale = brainStale, Mode = mode ?? (game ? "GAME" : "IDLE") },
        Profiles = new FancontrolProfileStatus { LastAppliedProfile = profile, Stale = profilesStale },
    };

    [Fact]
    public async Task Unconfigured_federation_falls_back_to_raw_guess()
    {
        var guesser = new FakeGuesser { Answer = "Gaming" };
        var auth = new ContextAuthorityService(new FakeStatus { Configured = false }, guesser);
        Assert.Equal("Gaming", await auth.DetectContextAsync());
        Assert.Equal(ContextSource.Guess, auth.LastSource);
        Assert.False(auth.FederationOwnsContext);
        Assert.False(guesser.LastSuppressGaming);
    }

    [Fact]
    public async Task Brain_game_flag_is_authoritative_gaming()
    {
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: true, profile: "Desktop") },
            new FakeGuesser { Answer = "Work" });
        Assert.Equal("Gaming", await auth.DetectContextAsync());
        Assert.Equal(ContextSource.Federation, auth.LastSource);
        Assert.True(auth.FederationOwnsContext);
    }

    [Theory]
    [InlineData("AAA-HDR")]
    [InlineData("aaa-sdr")]
    [InlineData("Competitive")]
    [InlineData("Benchmark")]
    public async Task Gaming_profile_plus_running_app_is_gaming_without_game_flag(string profile)
    {
        // Sub-150 W games (yesterday's Destiny session) run as mode=APP with game=false —
        // the gaming profile + an actually-running mapped app must still count as Gaming.
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: profile, mode: "APP") },
            new FakeGuesser { Answer = "Unknown" });
        Assert.Equal("Gaming", await auth.DetectContextAsync());
    }

    [Fact]
    public async Task Lingering_manual_gaming_profile_on_an_idle_night_is_not_gaming()
    {
        // Live-found during R4: a manually chosen AAA-HDR sticks as lastApplied for hours
        // after the game exits (manual profiles never auto-revert). NIGHT/IDLE + game=false
        // must beat the lingering profile — the mirror image of the audited guess bug.
        var guesser = new FakeGuesser { Answer = "Unknown" };
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: "AAA-HDR", mode: "NIGHT") },
            guesser);
        Assert.NotEqual("Gaming", await auth.DetectContextAsync());
        Assert.True(guesser.LastSuppressGaming);
    }

    [Fact]
    public async Task The_audited_failure_guess_says_gaming_while_brain_measures_idle()
    {
        // THE live-audited bug R4 exists to fix: steam.exe/discord.exe merely running made the
        // guesser say Gaming while the brain measured IDLE on the Desktop profile. The authority
        // must suppress the gaming guess and let the non-gaming signal (here: Plex) surface.
        var guesser = new FakeGuesser { Answer = "Gaming" };
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: "Desktop") },
            guesser);
        var ctx = await auth.DetectContextAsync();
        Assert.NotEqual("Gaming", ctx);
        Assert.True(guesser.LastSuppressGaming);
        Assert.Equal(ContextSource.Federation, auth.LastSource);
    }

    [Fact]
    public async Task NonGaming_federation_lets_plex_and_work_surface()
    {
        var guesser = new FakeGuesser { Answer = "Plex" };
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: "Film") },
            guesser);
        Assert.Equal("Plex", await auth.DetectContextAsync());
        Assert.True(guesser.LastSuppressGaming);
    }

    [Fact]
    public async Task BlueStacks_farming_is_not_gaming()
    {
        // BlueStacks is the farm app — farming must not pollute Gaming baselines.
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: "BlueStacks") },
            new FakeGuesser { Answer = "Unknown" });
        Assert.NotEqual("Gaming", await auth.DetectContextAsync());
    }

    [Fact]
    public async Task Fully_stale_federation_falls_back_to_raw_guess()
    {
        var guesser = new FakeGuesser { Answer = "Gaming" };
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: true, profile: "AAA-HDR", brainStale: true, profilesStale: true) },
            guesser);
        Assert.Equal("Gaming", await auth.DetectContextAsync());
        Assert.Equal(ContextSource.Guess, auth.LastSource);
        Assert.False(auth.FederationOwnsContext);
        Assert.False(guesser.LastSuppressGaming);
    }

    [Fact]
    public async Task Fresh_profiles_alone_carry_authority_when_brain_is_stale()
    {
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: true, profile: "Competitive", brainStale: true) },
            new FakeGuesser { Answer = "Unknown" });
        // Brain stale → its game flag is ignored; the fresh fgwatch profile still answers.
        Assert.Equal("Gaming", await auth.DetectContextAsync());
        Assert.Equal(ContextSource.Federation, auth.LastSource);
    }

    // ── R4 hard gate: fgwatch owns profile automation while the federation is fresh ──

    [Fact]
    public async Task ContextAutomation_is_hard_gated_when_federation_owns_context()
    {
        var auth = new ContextAuthorityService(
            new FakeStatus { Status = FreshStatus(game: false, profile: "Desktop") },
            new FakeGuesser { Answer = "Work" });

        var profileContext = new Mock<IProfileContextService>(MockBehavior.Strict);
        var profiles = new Mock<IProfileService>(MockBehavior.Strict);
        var settings = new Mock<ISettingsService>();
        // Even with the toggle ON (e.g. after the documented settings-reset trap), the gate holds.
        settings.Setup(s => s.Settings).Returns(new AppSettings { AutoContextSwitchEnabled = true, AutomationPaused = false });

        var svc = new ContextAutomationService(auth, profileContext.Object, profiles.Object, settings.Object);
        await svc.EvaluateAsync();

        profileContext.VerifyNoOtherCalls();   // strict: any automation attempt would have thrown
        profiles.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ContextAutomation_still_respects_settings_when_standalone()
    {
        var auth = new ContextAuthorityService(new FakeStatus { Configured = false }, new FakeGuesser { Answer = "Work" });
        var profileContext = new Mock<IProfileContextService>(MockBehavior.Strict);
        var profiles = new Mock<IProfileService>(MockBehavior.Strict);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Settings).Returns(new AppSettings { AutoContextSwitchEnabled = false });

        var svc = new ContextAutomationService(auth, profileContext.Object, profiles.Object, settings.Object);
        await svc.EvaluateAsync();   // disabled by settings → no calls

        profileContext.VerifyNoOtherCalls();
        profiles.VerifyNoOtherCalls();
    }
}
