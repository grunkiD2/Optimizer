using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class FancontrolProfileFollowerServiceTests
{
    private static FancontrolProfile Prof(string name, string optimizer)
        => new(name, 5, 50, false, "381b4222-f694-41f0-9685-ff5bb260df2e", "", "synapse", null, optimizer, "", "", false);

    private static SettingsProfile Preset(string id) => new() { Id = id, Name = id };

    private static FancontrolStatus StatusWith(string? lastApplied, bool stale = false)
        => new()
        {
            Profiles = new FancontrolProfileStatus
            {
                LastAppliedProfile = lastApplied,
                Stale = stale,
                Timestamp = DateTimeOffset.Now,
            }
        };

    private sealed class Harness
    {
        public bool Enabled = true;
        public string? Current = "Desktop";
        public bool Stale = false;
        public readonly Mock<IProfileService> Profiles = new();
        public readonly List<FancontrolProfile> ProfileDefs = new();
        public readonly FancontrolProfileFollowerService Svc;

        public Harness(IEnumerable<FancontrolProfile> defs, IEnumerable<SettingsProfile> presets)
        {
            ProfileDefs.AddRange(defs);
            var status = new Mock<IFancontrolStatusService>();
            status.SetupGet(s => s.IsConfigured).Returns(true);
            status.Setup(s => s.GetStatus()).Returns(() => StatusWith(Current, Stale));

            var commands = new Mock<IFancontrolCommandService>();
            commands.SetupGet(c => c.IsConfigured).Returns(true);
            commands.Setup(c => c.GetProfiles()).Returns(() => ProfileDefs);

            Profiles.SetupGet(p => p.BuiltInPresets).Returns(presets.ToList());
            Profiles.SetupGet(p => p.Snapshots).Returns(Array.Empty<SettingsProfile>());
            Profiles.Setup(p => p.ApplyPresetDetailedAsync(It.IsAny<string>(), It.IsAny<bool>()))
                .ReturnsAsync(new ProfileApplyResult());

            Svc = new FancontrolProfileFollowerService(status.Object, commands.Object, Profiles.Object, () => Enabled);
        }

        public Task<string?> Step() => Svc.FollowOnceAsync(CancellationToken.None);
        public void VerifyApplied(string presetId, Times times)
            => Profiles.Verify(p => p.ApplyPresetDetailedAsync(presetId, false), times);
        public void VerifyNeverApplied()
            => Profiles.Verify(p => p.ApplyPresetDetailedAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Disabled_does_nothing()
    {
        var h = new Harness([Prof("AAA-HDR", "preset-gaming")], [Preset("preset-gaming")]) { Enabled = false };
        h.Current = "Desktop"; await h.Step();
        h.Current = "AAA-HDR"; await h.Step();
        h.VerifyNeverApplied();
    }

    [Fact]
    public async Task Stale_federation_state_does_nothing()
    {
        var h = new Harness([Prof("AAA-HDR", "preset-gaming")], [Preset("preset-gaming")]) { Stale = true };
        h.Current = "Desktop"; await h.Step();
        h.Current = "AAA-HDR"; await h.Step();
        h.VerifyNeverApplied();
    }

    [Fact]
    public async Task First_observation_seeds_without_applying()
    {
        var h = new Harness([Prof("AAA-HDR", "preset-gaming")], [Preset("preset-gaming")]);
        h.Current = "AAA-HDR";
        Assert.Null(await h.Step());   // first observation = seed only
        h.VerifyNeverApplied();
    }

    [Fact]
    public async Task Switch_to_linked_profile_applies_its_preset()
    {
        var h = new Harness(
            [Prof("Desktop", ""), Prof("AAA-HDR", "preset-gaming")],
            [Preset("preset-gaming")]);
        h.Current = "Desktop"; await h.Step();        // seed (no link)
        h.Current = "AAA-HDR";
        Assert.Equal("preset-gaming", await h.Step()); // switch → apply
        h.VerifyApplied("preset-gaming", Times.Once());
    }

    [Fact]
    public async Task Switch_to_profile_without_link_does_not_apply()
    {
        var h = new Harness([Prof("Desktop", "preset-gaming"), Prof("Night", "")], [Preset("preset-gaming")]);
        h.Current = "Desktop"; await h.Step();   // seed
        h.Current = "Night"; await h.Step();      // no link → nothing
        h.VerifyNeverApplied();
    }

    [Fact]
    public async Task Unknown_preset_link_is_skipped()
    {
        var h = new Harness([Prof("Desktop", ""), Prof("AAA-HDR", "preset-nonexistent")], [Preset("preset-gaming")]);
        h.Current = "Desktop"; await h.Step();
        h.Current = "AAA-HDR"; Assert.Null(await h.Step());
        h.VerifyNeverApplied();
    }

    [Fact]
    public async Task Same_preset_across_profiles_is_idempotent()
    {
        var h = new Harness(
            [Prof("Desktop", ""), Prof("AAA-SDR", "preset-gaming"), Prof("Competitive", "preset-gaming"), Prof("Night", "preset-privacy")],
            [Preset("preset-gaming"), Preset("preset-privacy")]);
        h.Current = "Desktop"; await h.Step();                 // seed
        h.Current = "AAA-SDR"; await h.Step();                 // apply gaming
        h.Current = "Competitive"; Assert.Null(await h.Step()); // same preset → no-op
        h.Current = "Night"; await h.Step();                   // different preset → apply privacy
        h.VerifyApplied("preset-gaming", Times.Once());
        h.VerifyApplied("preset-privacy", Times.Once());
    }

    [Fact]
    public async Task Re_asserting_same_profile_does_not_apply()
    {
        var h = new Harness([Prof("Desktop", ""), Prof("AAA-HDR", "preset-gaming")], [Preset("preset-gaming")]);
        h.Current = "Desktop"; await h.Step();
        h.Current = "AAA-HDR"; await h.Step();   // apply
        await h.Step();                           // same profile again → no-op
        h.VerifyApplied("preset-gaming", Times.Once());
    }
}
