using System.Net.Http.Json;
using System.Text.Json;

namespace Optimizer.Cli;

/// <summary>
/// HTTP client for the Optimizer cloud server (Optimizer.Server).
/// Auth via X-Api-Key header — the API-key auth path is ideal for CLI/automation,
/// since it doesn't require an interactive magic-link session.
/// </summary>
public class CloudApiClient
{
    private readonly HttpClient _http;

    public CloudApiClient(string baseUrl, string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    /// <summary>
    /// Test-only factory — inject a pre-configured HttpClient (e.g. with a fake handler).
    /// </summary>
    public static CloudApiClient CreateForTesting(HttpClient http) => new(http);

    private CloudApiClient(HttpClient http) => _http = http;

    /// <summary>
    /// Create from environment variables.
    /// OPTIMIZER_CLOUD_URL  — defaults to http://localhost:5000
    /// OPTIMIZER_API_KEY    — required; create one in Settings → Developer
    /// </summary>
    public static CloudApiClient FromEnv()
    {
        var url    = Environment.GetEnvironmentVariable("OPTIMIZER_CLOUD_URL") ?? "http://localhost:5000";
        var apiKey = Environment.GetEnvironmentVariable("OPTIMIZER_API_KEY") ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("Error: OPTIMIZER_API_KEY environment variable is not set.");
            Console.Error.WriteLine("Create an API key in the desktop app → Settings → Developer,");
            Console.Error.WriteLine("or via POST /api/keys (requires JWT session).");
            Environment.Exit(1);
        }

        return new CloudApiClient(url, apiKey);
    }

    /// <summary>True when the server responds 200 on /api/health.</summary>
    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            var r = await _http.GetAsync("/api/health");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<JsonDocument?> GetAsync(string path)
    {
        try
        {
            var r = await _http.GetAsync(path);
            if (!r.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Cloud API error: {(int)r.StatusCode} {r.ReasonPhrase}");
                return null;
            }
            return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Cannot reach cloud server: {ex.Message}");
            Console.Error.WriteLine("Is OPTIMIZER_CLOUD_URL pointing to a running Optimizer.Server instance?");
            return null;
        }
    }

    public async Task<JsonDocument?> PostAsync(string path, object? body = null)
    {
        try
        {
            HttpResponseMessage r = body == null
                ? await _http.PostAsync(path, null)
                : await _http.PostAsJsonAsync(path, body);

            if (!r.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Cloud API error: {(int)r.StatusCode} {r.ReasonPhrase}");
                return null;
            }

            var content = await r.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(content)
                ? null
                : JsonDocument.Parse(content);
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Cannot reach cloud server: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string path)
    {
        try
        {
            var r = await _http.DeleteAsync(path);
            return r.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Cannot reach cloud server: {ex.Message}");
            return false;
        }
    }
}
