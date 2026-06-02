using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Optimizer.Services
{
    public record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string? LatestVersion, string? DownloadUrl, string Message);

    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckAsync();
    }

    /// <summary>
    /// Checks a configurable JSON feed for a newer version. The feed should return:
    /// { "version": "1.2.3.0", "url": "https://…/download" }.
    /// (Full MSIX auto-update uses Package.appinstaller + a publish URL; this is the lightweight
    /// in-app equivalent that works against any static JSON endpoint, e.g. a GitHub release asset.)
    /// </summary>
    public class UpdateService : IUpdateService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private readonly ISettingsService _settings;

        public UpdateService(ISettingsService settings) => _settings = settings;

        public async Task<UpdateCheckResult> CheckAsync()
        {
            var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";
            var feed = _settings.Settings.UpdateFeedUrl;

            if (string.IsNullOrWhiteSpace(feed))
            {
                return new UpdateCheckResult(false, current, null, null,
                    "No update feed configured. Set an update feed URL in Settings.");
            }

            try
            {
                var json = await Http.GetStringAsync(feed);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var latestStr = root.TryGetProperty("version", out var v) ? v.GetString() : null;
                var url = root.TryGetProperty("url", out var u) ? u.GetString() : null;

                if (!Version.TryParse(latestStr, out var latest) || !Version.TryParse(current, out var cur))
                {
                    return new UpdateCheckResult(false, current, latestStr, url, "Could not parse version information from the feed.");
                }

                return latest > cur
                    ? new UpdateCheckResult(true, current, latestStr, url, $"Update available: {latestStr} (you have {current}).")
                    : new UpdateCheckResult(false, current, latestStr, url, $"You're up to date ({current}).");
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult(false, current, null, null, $"Update check failed: {ex.Message}");
            }
        }
    }
}
