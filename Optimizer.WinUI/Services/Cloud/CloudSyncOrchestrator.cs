using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Cloud;

public class CloudSyncOrchestrator : ICloudSyncOrchestrator
{
    private readonly IOptimizerCloudClient _cloud;
    private readonly IProfileService _profiles;
    private readonly IHistoryService _history;
    private readonly ISettingsService _settings;
    private readonly ISyncTombstoneCollector _tombstones;
    private readonly string _cursorFile = Path.Combine(AppPaths.AppDataFolder, "cloud-cursor.json");
    private CursorState _state = new();

    // Debounce guard for settings-triggered syncs
    private DateTime _lastSettingsSync = DateTime.MinValue;
    private static readonly TimeSpan SettingsSyncDebounce = TimeSpan.FromSeconds(30);

    public CloudSyncOrchestrator(
        IOptimizerCloudClient cloud,
        IProfileService profiles,
        IHistoryService history,
        ISettingsService settings,
        ISyncTombstoneCollector tombstones)
    {
        _cloud = cloud;
        _profiles = profiles;
        _history = history;
        _settings = settings;
        _tombstones = tombstones;
        LoadState();

        // Subscribe to settings changes for debounced auto-sync
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public bool IsEnabled => _settings.Settings.CloudSyncEnabled;
    public DateTime? LastSyncAtUtc => _state.LastSyncAtUtc;
    public string? LastError => _state.LastError;
    public long CurrentCursor => _state.Cursor;

    public async Task<bool> SyncNowAsync()
    {
        if (!_cloud.IsAuthenticated)
        {
            _state.LastError = "Not authenticated";
            SaveState();
            return false;
        }

        try
        {
            // ── Pull: items newer than our cursor ──────────────────────────
            var pull = await _cloud.PullAsync(_state.Cursor);
            if (pull != null)
            {
                foreach (var item in pull.Items)
                    ApplyRemoteItem(item);
                _state.Cursor = pull.Cursor;
            }

            // ── Push: snapshots + history + settings + pending tombstones ──
            var pushItems = new List<CloudSyncItem>();

            // Snapshots
            foreach (var snap in _profiles.Snapshots)
            {
                pushItems.Add(new CloudSyncItem(
                    "snapshot",
                    snap.Id,
                    JsonSerializer.Serialize(snap)));
            }

            // History entries
            foreach (var entry in _history.Entries)
            {
                pushItems.Add(new CloudSyncItem(
                    "history",
                    entry.Id,
                    JsonSerializer.Serialize(entry)));
            }

            // Settings (single "main" item)
            pushItems.Add(new CloudSyncItem(
                "settings",
                "main",
                JsonSerializer.Serialize(_settings.Settings)));

            // Tombstones — locally deleted items not yet pushed
            var pendingTombstones = _tombstones.GetAndClear();
            foreach (var t in pendingTombstones)
            {
                pushItems.Add(new CloudSyncItem(t.ItemType, t.ItemId, "{}", IsDeleted: true));
            }

            if (pushItems.Count > 0)
            {
                var push = await _cloud.PushAsync(pushItems);
                if (push == null)
                {
                    // Push failed — re-queue the tombstones so they're not lost
                    foreach (var t in pendingTombstones)
                        _tombstones.Record(t.ItemType, t.ItemId);

                    _state.LastError = "Push failed";
                    SaveState();
                    return false;
                }
            }

            _state.LastError = null;
            _state.LastSyncAtUtc = DateTime.UtcNow;
            SaveState();
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud sync failed", ex);
            _state.LastError = ex.Message;
            SaveState();
            return false;
        }
    }

    public async Task EnableAsync()
    {
        _settings.Settings.CloudSyncEnabled = true;
        _settings.Save();
        await SyncNowAsync();
    }

    public Task DisableAsync()
    {
        _settings.Settings.CloudSyncEnabled = false;
        _settings.Save();
        return Task.CompletedTask;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void ApplyRemoteItem(CloudSyncItem item)
    {
        try
        {
            if (item.IsDeleted)
            {
                // Tombstone — delete the local copy
                switch (item.ItemType.ToLowerInvariant())
                {
                    case "snapshot":
                        _profiles.DeleteSnapshot(item.ItemId);
                        break;
                    case "history":
                        _history.DeleteEntry(item.ItemId);
                        break;
                    case "settings":
                        // Settings cannot be deleted, only merged — ignore
                        break;
                }
                return;
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            switch (item.ItemType.ToLowerInvariant())
            {
                case "snapshot":
                {
                    var snap = JsonSerializer.Deserialize<SettingsProfile>(item.Payload, opts);
                    if (snap != null) _profiles.UpsertSnapshot(snap);
                    break;
                }
                case "history":
                {
                    var entry = JsonSerializer.Deserialize<HistoryEntry>(item.Payload, opts);
                    if (entry != null) _history.UpsertEntry(entry);
                    break;
                }
                case "settings":
                {
                    if (item.ItemId == "main")
                    {
                        var remote = JsonSerializer.Deserialize<AppSettings>(item.Payload, opts);
                        if (remote != null) _settings.ApplyRemoteSettings(remote);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Apply remote item failed: {item.ItemType}/{item.ItemId}", ex);
        }
    }

    private void OnSettingsChanged()
    {
        if (!IsEnabled) return;
        if (DateTime.UtcNow - _lastSettingsSync < SettingsSyncDebounce) return;

        _lastSettingsSync = DateTime.UtcNow;
        _ = Task.Run(SyncNowAsync);
    }

    private void LoadState()
    {
        try
        {
            if (File.Exists(_cursorFile))
                _state = JsonSerializer.Deserialize<CursorState>(
                    File.ReadAllText(_cursorFile)) ?? new();
        }
        catch { _state = new(); }
    }

    private void SaveState()
    {
        try { File.WriteAllText(_cursorFile, JsonSerializer.Serialize(_state)); }
        catch { }
    }

    private sealed class CursorState
    {
        public long Cursor { get; set; }
        public DateTime? LastSyncAtUtc { get; set; }
        public string? LastError { get; set; }
    }
}
