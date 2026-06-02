using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Cloud;

public class CloudSyncOrchestrator : ICloudSyncOrchestrator
{
    private readonly IOptimizerCloudClient _cloud;
    private readonly IProfileService _profiles;
    private readonly ISettingsService _settings;
    private readonly string _cursorFile = Path.Combine(AppPaths.AppDataFolder, "cloud-cursor.json");
    private CursorState _state = new();

    public CloudSyncOrchestrator(
        IOptimizerCloudClient cloud,
        IProfileService profiles,
        ISettingsService settings)
    {
        _cloud = cloud;
        _profiles = profiles;
        _settings = settings;
        LoadState();
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
            // Pull items newer than our cursor
            var pull = await _cloud.PullAsync(_state.Cursor);
            if (pull != null)
            {
                foreach (var item in pull.Items)
                    ApplyRemoteItem(item);
                _state.Cursor = pull.Cursor;
            }

            // Push: gather all local snapshots + settings
            var pushItems = new List<CloudSyncItem>();

            foreach (var snap in _profiles.Snapshots)
            {
                pushItems.Add(new CloudSyncItem(
                    "snapshot",
                    snap.Id,
                    JsonSerializer.Serialize(snap)));
            }

            pushItems.Add(new CloudSyncItem(
                "settings",
                "main",
                JsonSerializer.Serialize(_settings.Settings)));

            if (pushItems.Count > 0)
            {
                var push = await _cloud.PushAsync(pushItems);
                if (push == null)
                {
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

    private void ApplyRemoteItem(CloudSyncItem item)
    {
        try
        {
            if (item.IsDeleted) return;  // tombstones deferred to V8.A3

            if (item.ItemType == "snapshot")
            {
                var snap = JsonSerializer.Deserialize<SettingsProfile>(item.Payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (snap != null)
                    _profiles.UpsertSnapshot(snap);
            }
            // settings and history apply deferred to V8.A3
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Apply remote item failed: {item.ItemType}/{item.ItemId}", ex);
        }
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
