using System.Diagnostics;
using System.Net.NetworkInformation;

namespace Optimizer.WinUI.Services;

public class NetworkSpeedTestService : INetworkSpeedTestService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public async Task<double> MeasureDownloadMbpsAsync(IProgress<double>? progress = null)
    {
        // 25 MB download
        var url = "https://speed.cloudflare.com/__down?bytes=25000000";
        var sw = Stopwatch.StartNew();
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                total += read;
                progress?.Report(total / 25_000_000.0);
            }
            sw.Stop();
            return (total * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0);
        }
        catch { return 0; }
    }

    public async Task<double> MeasureUploadMbpsAsync(IProgress<double>? progress = null)
    {
        // POST 10 MB of zeros
        var bytes = new byte[10_000_000];
        var sw = Stopwatch.StartNew();
        try
        {
            var content = new ByteArrayContent(bytes);
            using var response = await _http.PostAsync("https://speed.cloudflare.com/__up", content);
            sw.Stop();
            return (bytes.Length * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0);
        }
        catch { return 0; }
    }

    public async Task<(double pingMs, double jitterMs)> MeasurePingAsync()
    {
        var times = new List<double>();
        using var ping = new Ping();
        for (int i = 0; i < 5; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 1500);
                if (reply.Status == IPStatus.Success)
                    times.Add(reply.RoundtripTime);
            }
            catch { }
            await Task.Delay(200);
        }
        if (times.Count == 0) return (0, 0);
        var avg = times.Average();
        var jitter = times.Count > 1
            ? times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average()
            : 0;
        return (avg, jitter);
    }

    public async Task<SpeedTestResult> RunFullTestAsync(IProgress<string>? phaseProgress = null)
    {
        phaseProgress?.Report("Measuring ping…");
        var (ping, jitter) = await MeasurePingAsync();
        phaseProgress?.Report("Testing download…");
        var dl = await MeasureDownloadMbpsAsync();
        phaseProgress?.Report("Testing upload…");
        var ul = await MeasureUploadMbpsAsync();
        return new SpeedTestResult
        {
            DownloadMbps = dl,
            UploadMbps   = ul,
            PingMs       = ping,
            JitterMs     = jitter,
            TestedAt     = DateTime.Now
        };
    }
}
