using System.Net.Http.Json;
using System.Text.Json;

namespace Optimizer.Cli;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl, string token)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
    }

    public static ApiClient FromEnv()
    {
        var url = Environment.GetEnvironmentVariable("OPTIMIZER_URL") ?? "http://localhost:8765";
        var token = Environment.GetEnvironmentVariable("OPTIMIZER_TOKEN") ?? "";
        if (string.IsNullOrEmpty(token))
        {
            Console.Error.WriteLine("Error: OPTIMIZER_TOKEN environment variable not set.");
            Console.Error.WriteLine("Get your token from the Optimizer GUI Settings -> Remote API page.");
            Environment.Exit(1);
        }
        return new ApiClient(url, token);
    }

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
                Console.Error.WriteLine($"API error: {r.StatusCode}");
                return null;
            }
            return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            Console.Error.WriteLine("Is the Optimizer GUI running with API enabled?");
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
                Console.Error.WriteLine($"API error: {r.StatusCode}");
                return null;
            }
            return await JsonDocument.ParseAsync(await r.Content.ReadAsStreamAsync());
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"Connection failed: {ex.Message}");
            return null;
        }
    }
}
