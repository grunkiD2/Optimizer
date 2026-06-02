using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class UpdateService : IUpdateService
{
    private readonly IPowerShellRunner _psRunner;

    public UpdateService(IPowerShellRunner psRunner)
    {
        _psRunner = psRunner;
    }

    // ── Windows Update history ────────────────────────────────────────────────

    public async Task<IReadOnlyList<WindowsUpdateInfo>> GetRecentWindowsUpdatesAsync(int days = 60)
    {
        var list = new List<WindowsUpdateInfo>();
        try
        {
            var script = $@"
Get-HotFix |
  Where-Object {{ $_.InstalledOn -gt (Get-Date).AddDays(-{days}) }} |
  Select-Object Description, HotFixID, InstalledOn |
  ConvertTo-Json -Compress";

            var json = await _psRunner.RunAsync(script);
            if (string.IsNullOrWhiteSpace(json)) return list;

            // ConvertTo-Json returns an object when there is a single result
            using var doc = JsonDocument.Parse(NormalizeJsonArray(json));
            var root = doc.RootElement;

            foreach (var el in root.EnumerateArray())
            {
                var title = el.TryGetProperty("Description", out var d) ? d.GetString() ?? "" : "";
                var kb    = el.TryGetProperty("HotFixID", out var k)    ? k.GetString() ?? "" : "";

                DateTime installed = DateTime.MinValue;
                if (el.TryGetProperty("InstalledOn", out var ins) && ins.ValueKind == JsonValueKind.String)
                    DateTime.TryParse(ins.GetString(), out installed);

                list.Add(new WindowsUpdateInfo
                {
                    Title       = string.IsNullOrWhiteSpace(title) ? kb : title,
                    KbNumber    = kb,
                    InstalledOn = installed,
                    Status      = "Installed"
                });
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetRecentWindowsUpdatesAsync failed", ex);
        }

        return list.OrderByDescending(u => u.InstalledOn).ToList();
    }

    // ── winget app updates ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AppUpdateInfo>> GetWingetUpdatesAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<AppUpdateInfo>();
            try
            {
                // winget upgrade outputs a plain-text table; parse it manually
                var raw = RunProcess("winget", "upgrade --include-unknown --disable-interactivity", timeoutMs: 45_000);
                if (string.IsNullOrWhiteSpace(raw)) return list;

                // Find the header line that contains "Name" and "Id" columns
                var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                int headerIdx = -1;
                int nameStart = -1, idStart = -1, verStart = -1, availStart = -1, sourceStart = -1;

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.Contains("Name", StringComparison.OrdinalIgnoreCase) && line.Contains("Id", StringComparison.OrdinalIgnoreCase) && line.Contains("Version", StringComparison.OrdinalIgnoreCase) && line.Contains("Available", StringComparison.OrdinalIgnoreCase))
                    {
                        headerIdx   = i;
                        nameStart   = line.IndexOf("Name",      StringComparison.Ordinal);
                        idStart     = line.IndexOf("Id",        StringComparison.Ordinal);
                        verStart    = line.IndexOf("Version",   StringComparison.Ordinal);
                        availStart  = line.IndexOf("Available", StringComparison.Ordinal);
                        sourceStart = line.IndexOf("Source",    StringComparison.Ordinal);
                        break;
                    }
                }

                if (headerIdx < 0 || idStart < 0 || verStart < 0 || availStart < 0) return list;

                // Skip header + separator line
                for (int i = headerIdx + 2; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.Length < availStart + 1) continue;

                    // Skip lines that don't look like package rows
                    if (line.TrimStart().StartsWith('-') || line.Contains("upgrades available", StringComparison.OrdinalIgnoreCase) || line.Contains("No applicable", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string Extract(int start, int end)
                    {
                        if (start < 0 || start >= line.Length) return "";
                        var s = end > 0 && end <= line.Length ? line[start..end] : line[start..];
                        return s.Trim();
                    }

                    var name    = Extract(nameStart, idStart);
                    var id      = Extract(idStart, verStart);
                    var version = Extract(verStart, availStart);
                    var avail   = sourceStart > 0 ? Extract(availStart, sourceStart) : Extract(availStart, -1);
                    var source  = sourceStart > 0 ? Extract(sourceStart, -1) : "winget";

                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(avail)) continue;

                    list.Add(new AppUpdateInfo
                    {
                        Id               = id,
                        Name             = string.IsNullOrWhiteSpace(name) ? id : name,
                        CurrentVersion   = version,
                        AvailableVersion = avail,
                        Source           = string.IsNullOrWhiteSpace(source) ? "winget" : source
                    });
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error("GetWingetUpdatesAsync failed", ex);
            }

            return list;
        });
    }

    // ── Windows Update: open settings ────────────────────────────────────────

    public Task<bool> RunWindowsUpdateCheckAsync()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "ms-settings:windowsupdate-action",
                UseShellExecute = true
            });
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            EngineLog.Error("RunWindowsUpdateCheckAsync failed", ex);
            return Task.FromResult(false);
        }
    }

    // ── winget upgrade single app ─────────────────────────────────────────────

    public async Task<bool> UpgradeAppAsync(string appId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var args = $"upgrade --id \"{appId}\" --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
                var output = RunProcess("winget", args, timeoutMs: 120_000);
                return !output.Contains("failed", StringComparison.OrdinalIgnoreCase)
                    && !output.Contains("error", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                EngineLog.Error($"UpgradeAppAsync({appId}) failed", ex);
                return false;
            }
        });
    }

    // ── winget upgrade all ────────────────────────────────────────────────────

    public async Task<bool> UpgradeAllAppsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var args = "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
                var output = RunProcess("winget", args, timeoutMs: 300_000);
                return !output.Contains("failed", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                EngineLog.Error("UpgradeAllAppsAsync failed", ex);
                return false;
            }
        });
    }

    // ── BIOS info ─────────────────────────────────────────────────────────────

    public async Task<string> GetBiosInfoAsync()
    {
        try
        {
            var script = @"
$b = Get-WmiObject Win32_BIOS
$c = Get-WmiObject Win32_BaseBoard
[PSCustomObject]@{
    Manufacturer  = $b.Manufacturer
    Name          = $b.Name
    Version       = $b.Version
    ReleaseDate   = $b.ReleaseDate
    SerialNumber  = $b.SerialNumber
    BoardProduct  = $c.Product
    BoardMfr      = $c.Manufacturer
} | ConvertTo-Json -Compress";

            var json = await _psRunner.RunAsync(script);
            if (string.IsNullOrWhiteSpace(json)) return "BIOS information unavailable.";

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sb = new StringBuilder();
            AppendProp(sb, root, "Manufacturer", "BIOS Manufacturer");
            AppendProp(sb, root, "Name",         "BIOS Name");
            AppendProp(sb, root, "Version",      "BIOS Version");
            AppendProp(sb, root, "ReleaseDate",  "Release Date");
            AppendProp(sb, root, "SerialNumber", "Serial Number");
            AppendProp(sb, root, "BoardMfr",     "Board Manufacturer");
            AppendProp(sb, root, "BoardProduct", "Board Product");

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetBiosInfoAsync failed", ex);
            return "BIOS information unavailable.";
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AppendProp(StringBuilder sb, JsonElement root, string propName, string label)
    {
        if (root.TryGetProperty(propName, out var el) && el.ValueKind != JsonValueKind.Null)
        {
            var val = el.GetString();
            if (!string.IsNullOrWhiteSpace(val))
                sb.AppendLine($"{label}: {val}");
        }
    }

    private static string RunProcess(string exe, string args, int timeoutMs = 30_000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using var proc = Process.Start(psi)!;
            var sb = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.WaitForExit(timeoutMs);
            return sb.ToString();
        }
        catch { return ""; }
    }

    private static string NormalizeJsonArray(string json)
    {
        var trimmed = json.Trim();
        // Already an array
        if (trimmed.StartsWith('[')) return trimmed;
        // Single object — wrap it
        if (trimmed.StartsWith('{')) return $"[{trimmed}]";
        return "[]";
    }
}
