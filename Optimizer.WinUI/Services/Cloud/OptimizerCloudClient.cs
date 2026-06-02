using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Cloud;

public class OptimizerCloudClient : IOptimizerCloudClient
{
    private readonly HttpClient _http = new();
    private CloudSession? _session;
    private readonly string _sessionFile = Path.Combine(AppPaths.AppDataFolder, "cloud-session.json");

    public OptimizerCloudClient()
    {
        AppPaths.EnsureFolderExists();
        LoadSession();
    }

    public string? ServerUrl => _session?.ServerUrl;
    public bool IsAuthenticated => !string.IsNullOrEmpty(_session?.AccessToken);
    public string? CurrentUserEmail => _session?.Email;

    public async Task<bool> RequestMagicLinkAsync(string serverUrl, string email)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{serverUrl.TrimEnd('/')}/api/auth/request-magic-link",
                new { email, deviceName = Environment.MachineName });
            // Pre-stage the server URL so VerifyMagicLinkAsync knows where to send the token
            _session = new CloudSession { ServerUrl = serverUrl.TrimEnd('/'), Email = email };
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: magic link request failed", ex);
            return false;
        }
    }

    public async Task<bool> VerifyMagicLinkAsync(string token)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{_session.ServerUrl}/api/auth/verify",
                new { token });
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadFromJsonAsync<AuthBody>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (body == null) return false;
            _session.AccessToken = body.AccessToken;
            _session.RefreshToken = body.RefreshToken;
            _session.AccessTokenExpiry = body.ExpiresAtUtc;
            _session.Email = body.User.Email;
            _session.UserId = body.User.Id;
            SaveSession();
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: verify failed", ex);
            return false;
        }
    }

    public async Task LogoutAsync()
    {
        if (_session?.RefreshToken != null && _session.ServerUrl != null)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{_session.ServerUrl}/api/auth/logout");
                req.Content = JsonContent.Create(new { refreshToken = _session.RefreshToken });
                await _http.SendAsync(req);
            }
            catch { /* best-effort */ }
        }
        _session = null;
        try { if (File.Exists(_sessionFile)) File.Delete(_sessionFile); } catch { }
    }

    public async Task<SyncPullResult?> PullAsync(long cursor)
    {
        if (_session?.ServerUrl == null) return null;
        return await WithAuthRetryAsync(async () =>
        {
            using var req = NewAuthedRequest(HttpMethod.Get, $"{_session.ServerUrl}/api/sync?since={cursor}");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (resp.StatusCode, (SyncPullResult?)null);
            var body = await resp.Content.ReadFromJsonAsync<SyncPullBody>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (body == null) return (resp.StatusCode, null);
            var items = body.Items
                .Select(i => new CloudSyncItem(i.ItemType, i.ItemId, i.Payload, i.IsDeleted))
                .ToList();
            return (resp.StatusCode, new SyncPullResult(body.Cursor, items));
        });
    }

    public async Task<SyncPushResult?> PushAsync(IReadOnlyList<CloudSyncItem> items)
    {
        if (_session?.ServerUrl == null || items.Count == 0) return null;
        return await WithAuthRetryAsync(async () =>
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/sync");
            req.Content = JsonContent.Create(new { items });
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (resp.StatusCode, (SyncPushResult?)null);
            var body = await resp.Content.ReadFromJsonAsync<SyncPushBody>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (resp.StatusCode, body == null ? null : new SyncPushResult(body.ServerVersion));
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private HttpRequestMessage NewAuthedRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (_session?.AccessToken != null)
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _session.AccessToken);
        return req;
    }

    private async Task<T?> WithAuthRetryAsync<T>(
        Func<Task<(System.Net.HttpStatusCode Status, T? Result)>> action) where T : class
    {
        var (status, result) = await action();
        if (status == System.Net.HttpStatusCode.Unauthorized && _session?.RefreshToken != null)
        {
            if (await TryRefreshAsync())
                (_, result) = await action();
        }
        return result;
    }

    private async Task<bool> TryRefreshAsync()
    {
        if (_session?.ServerUrl == null || _session.RefreshToken == null) return false;
        try
        {
            using var resp = await _http.PostAsJsonAsync(
                $"{_session.ServerUrl}/api/auth/refresh",
                new { refreshToken = _session.RefreshToken });
            if (!resp.IsSuccessStatusCode) return false;
            var body = await resp.Content.ReadFromJsonAsync<AuthBody>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (body == null) return false;
            _session.AccessToken = body.AccessToken;
            _session.RefreshToken = body.RefreshToken;
            _session.AccessTokenExpiry = body.ExpiresAtUtc;
            SaveSession();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadSession()
    {
        try
        {
            if (File.Exists(_sessionFile))
                _session = JsonSerializer.Deserialize<CloudSession>(File.ReadAllText(_sessionFile));
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: session load failed", ex);
        }
    }

    private void SaveSession()
    {
        try
        {
            File.WriteAllText(_sessionFile, JsonSerializer.Serialize(_session));
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: session save failed", ex);
        }
    }

    // ── Internal DTOs ──────────────────────────────────────────────────────

    private class CloudSession
    {
        public string? ServerUrl { get; set; }
        public string? Email { get; set; }
        public string? UserId { get; set; }
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime AccessTokenExpiry { get; set; }
    }

    private record AuthBody(
        string AccessToken,
        string RefreshToken,
        DateTime ExpiresAtUtc,
        UserInfo User);

    private record UserInfo(string Id, string Email, string DisplayName);

    private record SyncPullBody(
        long Cursor,
        long ServerVersion,
        IReadOnlyList<SyncPullItem> Items);

    private record SyncPullItem(
        string ItemType,
        string ItemId,
        long Version,
        DateTime UpdatedAtUtc,
        string Payload,
        bool IsDeleted);

    private record SyncPushBody(long ServerVersion);
}
