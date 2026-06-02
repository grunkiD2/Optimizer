using System.Net.Http.Json;
using System.Text.Json;

namespace Optimizer.Mobile.Services;

/// <summary>
/// HTTP client for the Optimizer desktop REST API.
/// Configuration (URL + bearer token) is persisted via MAUI Preferences.
/// </summary>
public class ApiClient
{
    private HttpClient _http;

    public string ServerUrl => Preferences.Get("server_url", "");
    public string Token     => Preferences.Get("server_token", "");
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServerUrl) && !string.IsNullOrWhiteSpace(Token);

    public ApiClient()
    {
        _http = BuildClient();
    }

    // ── Configuration ──────────────────────────────────────────────────────

    public void SaveConfig(string url, string token)
    {
        Preferences.Set("server_url", url.TrimEnd('/'));
        Preferences.Set("server_token", token.Trim());
        _http = BuildClient();
    }

    public void Clear()
    {
        Preferences.Remove("server_url");
        Preferences.Remove("server_token");
        _http = BuildClient();
    }

    private HttpClient BuildClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        if (!string.IsNullOrWhiteSpace(Token))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    // ── API helpers ────────────────────────────────────────────────────────

    public async Task<T?> GetAsync<T>(string path) where T : class
    {
        if (!IsConfigured) return null;
        try
        {
            return await _http.GetFromJsonAsync<T>($"{ServerUrl}{path}");
        }
        catch { return null; }
    }

    public async Task<JsonElement?> GetJsonAsync(string path)
    {
        if (!IsConfigured) return null;
        try
        {
            var json = await _http.GetStringAsync($"{ServerUrl}{path}");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    public async Task<bool> PostAsync(string path)
    {
        if (!IsConfigured) return false;
        try
        {
            var r = await _http.PostAsync($"{ServerUrl}{path}", null);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Posts and returns the response body as a JsonElement, or null on failure.</summary>
    public async Task<JsonElement?> PostJsonAsync(string path)
    {
        if (!IsConfigured) return null;
        try
        {
            var r = await _http.PostAsync($"{ServerUrl}{path}", null);
            if (!r.IsSuccessStatusCode) return null;
            var json = await r.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    /// <summary>Tests connectivity to /api/health. Returns true when server is reachable and token is accepted.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        if (!IsConfigured) return false;
        try
        {
            var r = await _http.GetAsync($"{ServerUrl}/api/health");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>One-shot connection test with a supplied URL/token (before saving to prefs).</summary>
    public static async Task<bool> ProbeAsync(string url, string token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var r = await client.GetAsync($"{url.TrimEnd('/')}/api/health");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}
