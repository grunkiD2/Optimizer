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

    // ── Marketplace ────────────────────────────────────────────────────────

    public async Task<RemoteMarketplaceBrowseResult?> BrowseMarketplaceAsync(string? category, string? search, string? sort, int page, int pageSize)
    {
        if (_session?.ServerUrl == null) return null;
        try
        {
            var url = $"{_session.ServerUrl}/api/marketplace?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(category) && category != "All") url += $"&category={Uri.EscapeDataString(category)}";
            if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrEmpty(sort)) url += $"&sort={Uri.EscapeDataString(sort)}";

            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<MarketplaceBrowseBody>(opts);
            if (body == null) return null;
            var listings = body.Listings.Select(MapListing).ToList();
            return new RemoteMarketplaceBrowseResult(body.Total, body.Page, body.PageSize, listings);
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: marketplace browse failed", ex);
            return null;
        }
    }

    public async Task<RemoteMarketplaceListing?> GetMarketplaceListingAsync(string publicId)
    {
        if (_session?.ServerUrl == null) return null;
        try
        {
            using var resp = await _http.GetAsync($"{_session.ServerUrl}/api/marketplace/{publicId}");
            if (!resp.IsSuccessStatusCode) return null;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<MarketplaceListingBody>(opts);
            return body == null ? null : MapListing(body);
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: marketplace get listing failed", ex);
            return null;
        }
    }

    public async Task<bool> IncrementMarketplaceDownloadAsync(string publicId)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var resp = await _http.PostAsync($"{_session.ServerUrl}/api/marketplace/{publicId}/download", null);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: marketplace increment download failed", ex);
            return false;
        }
    }

    public async Task<bool> SubmitMarketplaceListingAsync(MarketplaceSubmission submission)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/marketplace/submit");
            req.Content = JsonContent.Create(new
            {
                name = submission.Name,
                description = submission.Description,
                category = submission.Category,
                tags = submission.Tags,
                optimizations = submission.Optimizations
            });
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && _session?.RefreshToken != null)
            {
                if (await TryRefreshAsync())
                {
                    using var req2 = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/marketplace/submit");
                    req2.Content = JsonContent.Create(new
                    {
                        name = submission.Name,
                        description = submission.Description,
                        category = submission.Category,
                        tags = submission.Tags,
                        optimizations = submission.Optimizations
                    });
                    using var resp2 = await _http.SendAsync(req2);
                    return resp2.IsSuccessStatusCode;
                }
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: marketplace submit failed", ex);
            return false;
        }
    }

    public async Task<bool> RateMarketplaceListingAsync(string publicId, int stars, string? comment)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/marketplace/{publicId}/rate");
            req.Content = JsonContent.Create(new { stars, comment });
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && _session?.RefreshToken != null)
            {
                if (await TryRefreshAsync())
                {
                    using var req2 = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/marketplace/{publicId}/rate");
                    req2.Content = JsonContent.Create(new { stars, comment });
                    using var resp2 = await _http.SendAsync(req2);
                    return resp2.IsSuccessStatusCode;
                }
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: marketplace rate failed", ex);
            return false;
        }
    }

    private static RemoteMarketplaceListing MapListing(MarketplaceListingBody b) => new(
        b.PublicId, b.Name, b.AuthorDisplayName, b.Description, b.Category,
        b.Tags, b.Optimizations, b.Downloads, b.AverageRating, b.RatingCount, b.Verified, b.Featured);

    // ── Plugin marketplace ─────────────────────────────────────────────────

    public async Task<RemotePluginBrowseResult?> BrowsePluginsAsync(string? category, string? search, string? sort, int page, int pageSize)
    {
        if (_session?.ServerUrl == null) return null;
        try
        {
            var url = $"{_session.ServerUrl}/api/plugins?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(category) && category != "All") url += $"&category={Uri.EscapeDataString(category)}";
            if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrEmpty(sort)) url += $"&sort={Uri.EscapeDataString(sort)}";

            using var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<PluginBrowseBody>(opts);
            if (body == null) return null;
            var listings = body.Listings.Select(MapPlugin).ToList();
            return new RemotePluginBrowseResult(body.Total, body.Page, body.PageSize, listings);
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: plugin browse failed", ex);
            return null;
        }
    }

    public async Task<RemotePluginDetail?> GetPluginDetailAsync(string pluginId)
    {
        if (_session?.ServerUrl == null) return null;
        try
        {
            using var resp = await _http.GetAsync($"{_session.ServerUrl}/api/plugins/{pluginId}");
            if (!resp.IsSuccessStatusCode) return null;
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<PluginDetailBody>(opts);
            return body == null ? null : MapPluginDetail(body);
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: plugin get detail failed", ex);
            return null;
        }
    }

    public async Task<bool> IncrementPluginDownloadAsync(string pluginId)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var resp = await _http.PostAsync($"{_session.ServerUrl}/api/plugins/{pluginId}/download", null);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: plugin increment download failed", ex);
            return false;
        }
    }

    public async Task<bool> SubmitPluginAsync(string manifestYaml)
    {
        if (_session?.ServerUrl == null) return false;
        try
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/plugins/submit");
            req.Content = JsonContent.Create(new { manifestYaml });
            using var resp = await _http.SendAsync(req);
            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized && _session?.RefreshToken != null)
            {
                if (await TryRefreshAsync())
                {
                    using var req2 = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/plugins/submit");
                    req2.Content = JsonContent.Create(new { manifestYaml });
                    using var resp2 = await _http.SendAsync(req2);
                    return resp2.IsSuccessStatusCode;
                }
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: plugin submit failed", ex);
            return false;
        }
    }

    // ── Federated Learning scaffold ────────────────────────────────────────

    public async Task<bool> ContributeFederatedAsync(IReadOnlyList<FederatedCategoryContribution> contributions)
    {
        if (_session?.ServerUrl == null || string.IsNullOrEmpty(_session.AccessToken)) return false;
        try
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/federated/contribute");
            req.Content = JsonContent.Create(new
            {
                contributions = contributions.Select(c => new
                {
                    category       = c.Category,
                    acceptanceRate = c.AcceptanceRate,
                    sampleWeight   = c.SampleWeight
                }).ToArray()
            });
            using var resp = await _http.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            EngineLog.Error("Cloud: federated contribute failed", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<FederatedCommunityBaseline>?> GetCommunityBaselinesAsync()
    {
        if (_session?.ServerUrl == null || string.IsNullOrEmpty(_session.AccessToken)) return null;
        return await WithAuthRetryAsync(async () =>
        {
            using var req = NewAuthedRequest(HttpMethod.Get, $"{_session.ServerUrl}/api/federated/baselines");
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return (resp.StatusCode, (IReadOnlyList<FederatedCommunityBaseline>?)null);
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var body = await resp.Content.ReadFromJsonAsync<FederatedBaselinesBody>(opts);
            if (body == null) return (resp.StatusCode, null);
            var baselines = body.Baselines
                .Select(b => new FederatedCommunityBaseline(b.Category, b.CommunityAcceptanceRate, b.ContributorCount))
                .ToList();
            return (resp.StatusCode, (IReadOnlyList<FederatedCommunityBaseline>?)baselines);
        });
    }

    // ── Event forwarding ──────────────────────────────────────────────────

    public async Task ForwardEventAsync(string type, string title, string detail, IReadOnlyDictionary<string, string>? data)
    {
        if (_session?.ServerUrl == null || string.IsNullOrEmpty(_session.AccessToken)) return;
        try
        {
            using var req = NewAuthedRequest(HttpMethod.Post, $"{_session.ServerUrl}/api/events");
            req.Content = JsonContent.Create(new
            {
                type,
                title,
                detail,
                timestampUtc = DateTime.UtcNow,
                data
            });
            using var resp = await _http.SendAsync(req);
            // Best-effort: ignore failures
        }
        catch
        {
            // Best-effort: swallow all failures
        }
    }

    private static RemotePluginListing MapPlugin(PluginListingBody b) => new(
        b.PluginId, b.Name, b.AuthorDisplayName, b.Description, b.Category,
        b.Downloads, b.AverageRating, b.RatingCount, b.Verified);

    private static RemotePluginDetail MapPluginDetail(PluginDetailBody b) => new(
        b.PluginId, b.Name, b.AuthorDisplayName, b.Description, b.Category,
        b.ManifestYaml, b.Signature, b.Verified, b.Downloads, b.AverageRating, b.RatingCount);

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

    private record MarketplaceBrowseBody(
        int Total,
        int Page,
        int PageSize,
        IReadOnlyList<MarketplaceListingBody> Listings);

    private record MarketplaceListingBody(
        Guid Id,
        string PublicId,
        string Name,
        string AuthorDisplayName,
        string Description,
        string Category,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Optimizations,
        int Downloads,
        double AverageRating,
        int RatingCount,
        bool Verified,
        bool Featured);

    private record PluginBrowseBody(
        int Total,
        int Page,
        int PageSize,
        IReadOnlyList<PluginListingBody> Listings);

    private record PluginListingBody(
        string PluginId,
        string Name,
        string AuthorDisplayName,
        string Description,
        string Category,
        int Downloads,
        double AverageRating,
        int RatingCount,
        bool Verified);

    private record PluginDetailBody(
        string PluginId,
        string Name,
        string AuthorDisplayName,
        string Description,
        string Category,
        string ManifestYaml,
        string? Signature,
        bool Verified,
        int Downloads,
        double AverageRating,
        int RatingCount);

    private record FederatedBaselinesBody(IReadOnlyList<FederatedBaselineItem> Baselines);
    private record FederatedBaselineItem(string Category, double CommunityAcceptanceRate, int ContributorCount);
}
