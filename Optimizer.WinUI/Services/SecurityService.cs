using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class SecurityService : ISecurityService
{
    private readonly IPowerShellRunner _psRunner;

    public SecurityService(IPowerShellRunner psRunner)
    {
        _psRunner = psRunner;
    }

    // ── Windows Defender ──────────────────────────────────────────────────────

    public async Task<DefenderStatus> GetDefenderStatusAsync()
    {
        var status = new DefenderStatus();
        try
        {
            var script = @"
$s = Get-MpComputerStatus
[PSCustomObject]@{
    RealTimeProtectionEnabled = $s.RealTimeProtectionEnabled
    CloudProtectionEnabled    = $s.MAPSReporting -gt 0
    TamperProtectionEnabled   = $s.IsTamperProtected
    LastQuickScan             = $s.QuickScanEndTime
    LastFullScan              = $s.FullScanEndTime
    DefinitionVersion         = $s.AntivirusSignatureVersion
} | ConvertTo-Json -Compress";

            var json = await _psRunner.RunAsync(script);
            if (string.IsNullOrWhiteSpace(json)) return status;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            status.RealTimeProtectionEnabled = GetBool(root, "RealTimeProtectionEnabled");
            status.CloudProtectionEnabled    = GetBool(root, "CloudProtectionEnabled");
            status.TamperProtectionEnabled   = GetBool(root, "TamperProtectionEnabled");
            status.DefinitionVersion         = GetString(root, "DefinitionVersion");

            if (root.TryGetProperty("LastQuickScan", out var qs) && qs.ValueKind == JsonValueKind.String
                && DateTime.TryParse(qs.GetString(), out var qsDt))
                status.LastQuickScan = qsDt;

            if (root.TryGetProperty("LastFullScan", out var fs) && fs.ValueKind == JsonValueKind.String
                && DateTime.TryParse(fs.GetString(), out var fsDt))
                status.LastFullScan = fsDt;
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetDefenderStatusAsync failed", ex);
        }
        return status;
    }

    // ── Firewall ──────────────────────────────────────────────────────────────

    public async Task<FirewallStatus> GetFirewallStatusAsync()
    {
        var status = new FirewallStatus();
        try
        {
            var script = @"
Get-NetFirewallProfile |
  Select-Object Name, Enabled |
  ConvertTo-Json -Compress";

            var json = await _psRunner.RunAsync(script);
            if (string.IsNullOrWhiteSpace(json)) return status;

            var normalized = json.Trim().StartsWith('[') ? json : $"[{json}]";
            using var doc = JsonDocument.Parse(normalized);

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var name    = GetString(el, "Name");
                var enabled = GetBool(el, "Enabled");
                switch (name)
                {
                    case "Domain":  status.DomainEnabled  = enabled; break;
                    case "Private": status.PrivateEnabled = enabled; break;
                    case "Public":  status.PublicEnabled  = enabled; break;
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetFirewallStatusAsync failed", ex);
        }
        return status;
    }

    // ── BitLocker ─────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BitLockerVolume>> GetBitLockerStatusAsync()
    {
        var list = new List<BitLockerVolume>();
        try
        {
            var script = @"
Get-BitLockerVolume |
  Select-Object MountPoint, ProtectionStatus, EncryptionMethod, LockStatus |
  ConvertTo-Json -Compress";

            var json = await _psRunner.RunAsync(script);
            if (string.IsNullOrWhiteSpace(json)) return list;

            var normalized = json.Trim().StartsWith('[') ? json : $"[{json}]";
            using var doc = JsonDocument.Parse(normalized);

            foreach (var el in doc.RootElement.EnumerateArray())
            {
                list.Add(new BitLockerVolume
                {
                    DriveLetter      = GetString(el, "MountPoint"),
                    ProtectionStatus = GetString(el, "ProtectionStatus"),
                    EncryptionMethod = GetString(el, "EncryptionMethod"),
                    LockStatus       = GetString(el, "LockStatus")
                });
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error("GetBitLockerStatusAsync failed", ex);
        }
        return list;
    }

    // ── Composite score ───────────────────────────────────────────────────────

    public async Task<int> GetSecurityScoreAsync()
    {
        try
        {
            var defTask = GetDefenderStatusAsync();
            var fwTask  = GetFirewallStatusAsync();
            var blTask  = GetBitLockerStatusAsync();

            await Task.WhenAll(defTask, fwTask, blTask);

            var def = defTask.Result;
            var fw  = fwTask.Result;
            var bl  = blTask.Result;

            int score = 0;

            // Defender: 3 factors × ~13 pts each → max 39 pts (rounded to 40)
            if (def.RealTimeProtectionEnabled) score += 20;
            if (def.CloudProtectionEnabled)    score += 10;
            if (def.TamperProtectionEnabled)   score += 10;

            // Firewall: 3 profiles × ~10 pts each → max 30 pts
            if (fw.DomainEnabled)  score += 10;
            if (fw.PrivateEnabled) score += 10;
            if (fw.PublicEnabled)  score += 10;

            // BitLocker: system drive protected → 20 pts
            var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))?.TrimEnd('\\') ?? "C:";
            var sysBl = bl.FirstOrDefault(v =>
                v.DriveLetter.TrimEnd('\\').Equals(systemDrive, StringComparison.OrdinalIgnoreCase));
            if (sysBl != null &&
                (sysBl.ProtectionStatus.Contains("On", StringComparison.OrdinalIgnoreCase)
                 || sysBl.ProtectionStatus == "1"))
                score += 20;

            return Math.Min(score, 100);
        }
        catch
        {
            return 0;
        }
    }

    // ── Quick scan ────────────────────────────────────────────────────────────

    public async Task<bool> RunQuickScanAsync()
    {
        try
        {
            await _psRunner.RunAsync("Start-MpScan -ScanType QuickScan");
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Error("RunQuickScanAsync failed", ex);
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool   GetBool(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;

    private static string GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? (v.GetString() ?? "") : "";
}
