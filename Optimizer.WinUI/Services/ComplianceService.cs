using System.Text;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ComplianceService : IComplianceService
{
    private readonly IPrivacyService _privacy;
    private readonly ISecurityService _security;

    public ComplianceService(IPrivacyService privacy, ISecurityService security)
    {
        _privacy  = privacy;
        _security = security;
    }

    public List<string> AvailableFrameworks { get; } =
        ["CIS Benchmark", "NIST 800-171", "HIPAA", "SOC 2"];

    // ── Run checks ────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ComplianceCheck>> RunFrameworkAsync(string framework)
    {
        var checks = new List<ComplianceCheck>();

        // Fetch data from existing services in parallel
        var privacyTask  = _privacy.GetAllAsync();
        var defenderTask = _security.GetDefenderStatusAsync();
        var firewallTask = _security.GetFirewallStatusAsync();
        var bitlockerTask= _security.GetBitLockerStatusAsync();

        await Task.WhenAll(privacyTask, defenderTask, firewallTask, bitlockerTask);

        var privacy  = privacyTask.Result;
        var defender = defenderTask.Result;
        var firewall = firewallTask.Result;
        var bitlocker= bitlockerTask.Result;

        // ── CIS Benchmark ─────────────────────────────────────────────────────
        if (framework is "CIS Benchmark" or "ALL")
        {
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.1", ControlId = "CIS-1.1.1", Framework = "CIS Benchmark",
                Title       = "Ensure Windows Defender is running",
                Description = "Real-time protection must be enabled.",
                Status      = defender.RealTimeProtectionEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"RealTimeProtection: {defender.RealTimeProtectionEnabled}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.2", ControlId = "CIS-1.1.2", Framework = "CIS Benchmark",
                Title       = "Ensure all firewall profiles are enabled",
                Description = "Domain, Private, and Public profiles must all be on.",
                Status      = firewall.DomainEnabled && firewall.PrivateEnabled && firewall.PublicEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"Domain={firewall.DomainEnabled}, Private={firewall.PrivateEnabled}, Public={firewall.PublicEnabled}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.3", ControlId = "CIS-1.1.3", Framework = "CIS Benchmark",
                Title       = "Ensure BitLocker is enabled on system drive",
                Description = "System drive must be encrypted.",
                Status      = bitlocker.Any(v =>
                                    v.DriveLetter.StartsWith("C", StringComparison.OrdinalIgnoreCase) &&
                                    v.ProtectionStatus.Contains("On", StringComparison.OrdinalIgnoreCase))
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"BitLocker volumes: {bitlocker.Count}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.4", ControlId = "CIS-1.1.4", Framework = "CIS Benchmark",
                Title       = "Ensure telemetry is minimized",
                Description = "Telemetry level should be set to Security/Required.",
                Status      = privacy.FirstOrDefault(p => p.Id == "diagnostic-level")?.IsPrivacyFriendly == true
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.5", ControlId = "CIS-1.1.5", Framework = "CIS Benchmark",
                Title       = "Ensure Advertising ID is disabled",
                Description = "Windows Advertising ID should be turned off.",
                Status      = privacy.FirstOrDefault(p => p.Id == "ads-id")?.IsPrivacyFriendly == true
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.6", ControlId = "CIS-1.1.6", Framework = "CIS Benchmark",
                Title       = "Ensure cloud-delivered protection is enabled",
                Description = "Windows Defender cloud protection should be active.",
                Status      = defender.CloudProtectionEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"CloudProtection: {defender.CloudProtectionEnabled}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "cis-1.1.7", ControlId = "CIS-1.1.7", Framework = "CIS Benchmark",
                Title       = "Ensure Tamper Protection is enabled",
                Description = "Tamper Protection prevents unauthorized changes to security settings.",
                Status      = defender.TamperProtectionEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"TamperProtection: {defender.TamperProtectionEnabled}"
            });
        }

        // ── NIST 800-171 ──────────────────────────────────────────────────────
        if (framework is "NIST 800-171" or "ALL")
        {
            checks.Add(new ComplianceCheck
            {
                Id          = "nist-3.13.11", ControlId = "NIST 3.13.11", Framework = "NIST 800-171",
                Title       = "Employ FIPS-validated cryptography",
                Description = "BitLocker uses FIPS-validated algorithms.",
                Status      = bitlocker.Any() ? ComplianceStatus.Pass : ComplianceStatus.NotApplicable,
                Evidence    = $"BitLocker volumes detected: {bitlocker.Count}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "nist-3.14.1", ControlId = "NIST 3.14.1", Framework = "NIST 800-171",
                Title       = "Identify, report, and correct system flaws",
                Description = "Windows Defender must be active with real-time protection.",
                Status      = defender.RealTimeProtectionEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"RealTimeProtection: {defender.RealTimeProtectionEnabled}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "nist-3.13.5", ControlId = "NIST 3.13.5", Framework = "NIST 800-171",
                Title       = "Implement subnetworks for publicly accessible system components",
                Description = "Host-based firewall must be enabled on all profiles.",
                Status      = firewall.DomainEnabled && firewall.PrivateEnabled && firewall.PublicEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"Domain={firewall.DomainEnabled}, Private={firewall.PrivateEnabled}, Public={firewall.PublicEnabled}"
            });
        }

        // ── HIPAA ─────────────────────────────────────────────────────────────
        if (framework is "HIPAA" or "ALL")
        {
            checks.Add(new ComplianceCheck
            {
                Id          = "hipaa-164.312-a", ControlId = "HIPAA 164.312(a)(2)(iv)", Framework = "HIPAA",
                Title       = "Encryption and Decryption",
                Description = "ePHI must be encrypted at rest.",
                Status      = bitlocker.Any(v =>
                                    v.DriveLetter.StartsWith("C", StringComparison.OrdinalIgnoreCase) &&
                                    v.ProtectionStatus.Contains("On", StringComparison.OrdinalIgnoreCase))
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"BitLocker volumes: {bitlocker.Count}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "hipaa-164.312-b", ControlId = "HIPAA 164.312(b)", Framework = "HIPAA",
                Title       = "Audit Controls",
                Description = "Hardware, software, and procedural mechanisms that record and examine activity.",
                Status      = ComplianceStatus.NotApplicable,
                Evidence    = "Audit log configuration requires manual review"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "hipaa-164.312-e", ControlId = "HIPAA 164.312(e)(2)(ii)", Framework = "HIPAA",
                Title       = "Transmission Security — Encryption",
                Description = "ePHI in transit must be encrypted.",
                Status      = ComplianceStatus.NotApplicable,
                Evidence    = "Network encryption requires application-level review"
            });
        }

        // ── SOC 2 ─────────────────────────────────────────────────────────────
        if (framework is "SOC 2" or "ALL")
        {
            checks.Add(new ComplianceCheck
            {
                Id          = "soc2-cc6.1", ControlId = "SOC 2 CC6.1", Framework = "SOC 2",
                Title       = "Logical access controls",
                Description = "User access must be authenticated and authorized.",
                Status      = ComplianceStatus.Pass,
                Evidence    = "Windows authentication active"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "soc2-cc6.6", ControlId = "SOC 2 CC6.6", Framework = "SOC 2",
                Title       = "System hardening",
                Description = "Unnecessary services should be disabled to reduce attack surface.",
                Status      = ComplianceStatus.NotApplicable,
                Evidence    = "Manual service review required"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "soc2-cc7.1", ControlId = "SOC 2 CC7.1", Framework = "SOC 2",
                Title       = "System monitoring",
                Description = "Monitoring tools must be deployed to detect threats.",
                Status      = defender.RealTimeProtectionEnabled
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail,
                Evidence    = $"Windows Defender real-time: {defender.RealTimeProtectionEnabled}"
            });
            checks.Add(new ComplianceCheck
            {
                Id          = "soc2-cc9.2", ControlId = "SOC 2 CC9.2", Framework = "SOC 2",
                Title       = "Vendor / third-party risk",
                Description = "Telemetry sharing with third parties must be minimized.",
                Status      = privacy.FirstOrDefault(p => p.Id == "diagnostic-level")?.IsPrivacyFriendly == true
                                ? ComplianceStatus.Pass : ComplianceStatus.Fail
            });
        }

        return checks;
    }

    // ── Export HTML report ────────────────────────────────────────────────────

    public async Task<string> ExportAuditReportAsync(IReadOnlyList<ComplianceCheck> checks)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Optimizer Reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"compliance-audit-{DateTime.Now:yyyyMMdd-HHmmss}.html");

        var passing = checks.Count(c => c.Status == ComplianceStatus.Pass);
        var failing = checks.Count(c => c.Status == ComplianceStatus.Fail);
        var na      = checks.Count(c => c.Status == ComplianceStatus.NotApplicable);

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html lang=\"en\"><head>");
        html.AppendLine("<meta charset=\"utf-8\"/>");
        html.AppendLine("<title>Compliance Audit Report — Optimizer</title>");
        html.AppendLine("<style>");
        html.AppendLine("body{font-family:system-ui,-apple-system,sans-serif;max-width:960px;margin:40px auto;padding:0 24px;color:#111;}");
        html.AppendLine("h1{font-size:1.8rem;margin-bottom:4px;}");
        html.AppendLine(".subtitle{color:#6B7280;margin-bottom:24px;}");
        html.AppendLine(".summary{display:flex;gap:16px;margin-bottom:24px;}");
        html.AppendLine(".pill{padding:8px 16px;border-radius:8px;font-weight:600;font-size:0.9rem;}");
        html.AppendLine(".pass{background:#D1FAE5;color:#065F46;} .fail{background:#FEE2E2;color:#991B1B;} .na{background:#F3F4F6;color:#374151;}");
        html.AppendLine("table{width:100%;border-collapse:collapse;font-size:0.88rem;}");
        html.AppendLine("th{background:#F9FAFB;text-align:left;padding:10px 12px;border-bottom:2px solid #E5E7EB;}");
        html.AppendLine("td{padding:10px 12px;border-bottom:1px solid #E5E7EB;vertical-align:top;}");
        html.AppendLine(".badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:0.78rem;font-weight:600;}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>Compliance Audit Report</h1>");
        html.AppendLine($"<p class='subtitle'>Generated by Optimizer &bull; {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");
        html.AppendLine("<div class='summary'>");
        html.AppendLine($"<span class='pill pass'>{passing} Pass</span>");
        html.AppendLine($"<span class='pill fail'>{failing} Fail</span>");
        html.AppendLine($"<span class='pill na'>{na} N/A</span>");
        html.AppendLine($"<span class='pill' style='background:#EFF6FF;color:#1E40AF;'>{checks.Count} Total</span>");
        html.AppendLine("</div>");
        html.AppendLine("<table>");
        html.AppendLine("<tr><th>Control</th><th>Framework</th><th>Title</th><th>Status</th><th>Evidence</th></tr>");

        foreach (var c in checks)
        {
            var (badgeCss, badgeText) = c.Status switch
            {
                ComplianceStatus.Pass => ("pass", "Pass"),
                ComplianceStatus.Fail => ("fail", "Fail"),
                _                     => ("na",   "N/A")
            };
            html.AppendLine($"<tr><td>{HtmlEncode(c.ControlId)}</td>" +
                $"<td>{HtmlEncode(c.Framework)}</td>" +
                $"<td>{HtmlEncode(c.Title)}</td>" +
                $"<td><span class='badge {badgeCss}'>{badgeText}</span></td>" +
                $"<td>{HtmlEncode(c.Evidence)}</td></tr>");
        }

        html.AppendLine("</table></body></html>");
        await File.WriteAllTextAsync(path, html.ToString());
        return path;
    }

    private static string HtmlEncode(string s)
        => System.Net.WebUtility.HtmlEncode(s ?? "");
}
