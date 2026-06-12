using System;
using System.IO;
using System.Threading.Tasks;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Analytics;
using Optimizer.WinUI.Services.Data;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>Integration tests for the confirm-on-first-occurrence auto-apply gate over a temp SQLite DB.</summary>
public class AutoApplyPolicyTests : IAsyncLifetime, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"optimizer-test-{Guid.NewGuid():N}.db");
    private DatabaseService _db = null!;

    private sealed class FakeSettings(AppSettings settings) : ISettingsService
    {
        public AppSettings Settings { get; } = settings;
        public event Action? SettingsChanged;
        public void Load() { }
        public void Save() => SettingsChanged?.Invoke();
        public void Reset() { }
    }

    public async Task InitializeAsync()
    {
        _db = new DatabaseService(_dbPath);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    private AutoApplyPolicy NewPolicy(AppSettings settings) => new(_db, new FakeSettings(settings));

    [Fact]
    public async Task Blocks_until_success_threshold_reached()
    {
        var settings = new AppSettings { AutoApplyEnabled = true, AutoApplySuccessThreshold = 3 };
        var policy = NewPolicy(settings);

        Assert.False(await policy.CanAutoApplyAsync("opt-x", "Gaming"));

        await policy.RecordOutcomeAsync("opt-x", "Gaming", success: true);
        await policy.RecordOutcomeAsync("opt-x", "Gaming", success: true);
        Assert.False(await policy.CanAutoApplyAsync("opt-x", "Gaming")); // only 2

        await policy.RecordOutcomeAsync("opt-x", "Gaming", success: true);
        Assert.True(await policy.CanAutoApplyAsync("opt-x", "Gaming"));  // 3 => allowed
    }

    [Fact]
    public async Task Any_failure_blocks_auto_apply()
    {
        var settings = new AppSettings { AutoApplyEnabled = true, AutoApplySuccessThreshold = 1 };
        var policy = NewPolicy(settings);

        await policy.RecordOutcomeAsync("opt-y", "Work", success: true);
        await policy.RecordOutcomeAsync("opt-y", "Work", success: false);

        Assert.False(await policy.CanAutoApplyAsync("opt-y", "Work"));
    }

    [Fact]
    public async Task Master_kill_switch_and_toggle_and_exclusion_all_block()
    {
        async Task<bool> CanWith(Action<AppSettings> tweak)
        {
            var s = new AppSettings { AutoApplyEnabled = true, AutoApplySuccessThreshold = 1 };
            tweak(s);
            var p = NewPolicy(s);
            await p.RecordOutcomeAsync("opt-z", "Plex", success: true);
            return await p.CanAutoApplyAsync("opt-z", "Plex");
        }

        Assert.False(await CanWith(s => s.AutomationPaused = true));
        Assert.False(await CanWith(s => s.AutoApplyEnabled = false));
        Assert.False(await CanWith(s => s.AutoApplyExcluded.Add("opt-z")));
        Assert.True(await CanWith(_ => { })); // baseline: allowed
    }

    [Fact]
    public async Task Context_is_isolated()
    {
        var settings = new AppSettings { AutoApplyEnabled = true, AutoApplySuccessThreshold = 1 };
        var policy = NewPolicy(settings);

        await policy.RecordOutcomeAsync("opt-c", "Gaming", success: true);

        Assert.True(await policy.CanAutoApplyAsync("opt-c", "Gaming"));
        Assert.False(await policy.CanAutoApplyAsync("opt-c", "Work")); // no history in Work
    }
}
