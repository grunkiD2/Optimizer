using System.Net.NetworkInformation;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services.Diagnostics;

namespace Optimizer.WinUI.Services;

/// <summary>
/// Thin coordinator: collects findings from all registered <see cref="IDiagnosticPlugin"/>
/// implementations. Adding a new diagnostic category only requires creating a new plugin class
/// and registering it in DI — this class needs no modification.
/// </summary>
public class DiagnosticsService : IDiagnosticsService
{
    private readonly IEnumerable<IDiagnosticPlugin> _plugins;

    public DiagnosticsService(IEnumerable<IDiagnosticPlugin> plugins)
    {
        _plugins = plugins;
    }

    public Task<IReadOnlyList<DiagnosticFinding>> RunQuickScanAsync()
        => RunAsync(DiagnosticScanLevel.Quick, null);

    public Task<IReadOnlyList<DiagnosticFinding>> RunFullScanAsync(IProgress<string>? progress = null)
        => RunAsync(DiagnosticScanLevel.Full, progress);

    private async Task<IReadOnlyList<DiagnosticFinding>> RunAsync(DiagnosticScanLevel level, IProgress<string>? progress)
    {
        var findings = new List<DiagnosticFinding>();
        foreach (var plugin in _plugins.Where(p => p.SupportedLevels.HasFlag(level)))
        {
            progress?.Report($"Running: {plugin.Name}...");
            try
            {
                var pluginFindings = await plugin.RunAsync(progress);
                findings.AddRange(pluginFindings);
            }
            catch (Exception ex)
            {
                EngineLog.Error($"Diagnostic plugin '{plugin.Name}' failed", ex);
            }
        }
        return findings;
    }

    // ── Network deep diagnostics ────────────────────────────────────────────
    // Kept as a standalone method: it has its own UI flow and is not a scan category.

    public async Task<IReadOnlyList<NetworkDiagnostic>> RunNetworkDeepAsync(IProgress<string>? progress = null)
    {
        var results = new List<NetworkDiagnostic>();

        // 1. Traceroute to 1.1.1.1 (up to 30 hops)
        progress?.Report("Tracing route to 1.1.1.1...");
        try
        {
            using var ping = new Ping();
            var routeLines = new List<string>();
            for (int ttl = 1; ttl <= 30; ttl++)
            {
                var options = new PingOptions(ttl, true);
                try
                {
                    var reply = await ping.SendPingAsync("1.1.1.1", 2000, new byte[32], options);
                    var addr = reply.Address?.ToString() ?? "*";
                    routeLines.Add($"  {ttl,2}: {addr} ({reply.RoundtripTime} ms) [{reply.Status}]");
                    if (reply.Status == IPStatus.Success) break;
                }
                catch (Exception ex)
                {
                    routeLines.Add($"  {ttl,2}: [{ex.Message}]");
                    break;
                }
            }
            results.Add(new NetworkDiagnostic
            {
                Type = "Traceroute",
                Target = "1.1.1.1",
                Result = routeLines.Count > 0 ? string.Join("\n", routeLines) : "No hops recorded",
                Success = routeLines.Count > 0
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "Traceroute", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        // 2. MTU discovery
        progress?.Report("Discovering MTU...");
        try
        {
            int optimalMtu = await FindMtuAsync();
            results.Add(new NetworkDiagnostic
            {
                Type = "MTU",
                Target = "1.1.1.1",
                Result = optimalMtu > 0 ? $"Maximum MTU: {optimalMtu} bytes" : "Could not determine MTU",
                Success = optimalMtu > 0
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "MTU", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        // 3. Packet loss (100 pings)
        progress?.Report("Testing packet loss (100 pings)...");
        try
        {
            using var ping2 = new Ping();
            int successCount = 0;
            long totalTime = 0;
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var reply = await ping2.SendPingAsync("1.1.1.1", 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        successCount++;
                        totalTime += reply.RoundtripTime;
                    }
                }
                catch { }
            }
            var lossPct = 100 - successCount;
            var avgMs = successCount > 0 ? totalTime / successCount : 0;
            results.Add(new NetworkDiagnostic
            {
                Type = "Packet Loss",
                Target = "1.1.1.1",
                Result = $"{lossPct}% loss ({successCount}/100 successful), avg {avgMs} ms",
                Success = lossPct < 5
            });
        }
        catch (Exception ex)
        {
            results.Add(new NetworkDiagnostic
            {
                Type = "Packet Loss", Target = "1.1.1.1",
                Result = $"Failed: {ex.Message}", Success = false
            });
        }

        return results;
    }

    private static async Task<int> FindMtuAsync()
    {
        using var ping = new Ping();
        int low = 500, high = 1472, best = 0;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            var buffer = new byte[mid];
            var options = new PingOptions(64, true); // DontFragment = true
            try
            {
                var reply = await ping.SendPingAsync("1.1.1.1", 1500, buffer, options);
                if (reply.Status == IPStatus.Success)
                {
                    best = mid + 28;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            catch
            {
                high = mid - 1;
            }
        }
        return best;
    }
}
