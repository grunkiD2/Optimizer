using System.Text;
using System.Text.Json;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class ReportService : IReportService
{
    private readonly IHardwareInfoService    _hardware;
    private readonly IDiskHealthService      _diskHealth;
    private readonly IDiagnosticsService     _diagnostics;
    private readonly IHistoryService          _history;
    private readonly ISecurityService        _security;

    public ReportService(
        IHardwareInfoService hardware,
        IDiskHealthService   diskHealth,
        IDiagnosticsService  diagnostics,
        IHistoryService       history,
        ISecurityService     security)
    {
        _hardware    = hardware;
        _diskHealth  = diskHealth;
        _diagnostics = diagnostics;
        _history     = history;
        _security    = security;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<GeneratedReport> GenerateAsync(ReportType type, ReportFormat format)
    {
        var report = new GeneratedReport
        {
            Title     = ReportTitle(type),
            Generated = DateTime.Now,
            Format    = format
        };

        report.Content = format switch
        {
            ReportFormat.Text => await BuildTextAsync(type),
            ReportFormat.Html => await BuildHtmlAsync(type),
            ReportFormat.Json => await BuildJsonAsync(type),
            _ => ""
        };

        return report;
    }

    public async Task<string> SaveReportAsync(GeneratedReport report)
    {
        var ext = report.Format switch
        {
            ReportFormat.Html => ".html",
            ReportFormat.Json => ".json",
            _                 => ".txt"
        };

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Optimizer Reports");
        Directory.CreateDirectory(dir);

        var stamp    = report.Generated.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeName = report.Title.Replace(' ', '-').Replace('/', '-');
        var path     = Path.Combine(dir, $"{safeName}_{stamp}{ext}");

        await File.WriteAllTextAsync(path, report.Content, Encoding.UTF8);
        return path;
    }

    // ── Title helpers ─────────────────────────────────────────────────────────

    private static string ReportTitle(ReportType type) => type switch
    {
        ReportType.SystemSnapshot  => "System Snapshot",
        ReportType.HealthReport    => "Health Report",
        ReportType.OptimizationLog => "Optimization Log",
        ReportType.FullReport      => "Full System Report",
        _ => "Report"
    };

    // ── Text builder ──────────────────────────────────────────────────────────

    private async Task<string> BuildTextAsync(ReportType type)
    {
        var sb = new StringBuilder();

        if (type is ReportType.SystemSnapshot or ReportType.FullReport)
            await AppendSnapshotText(sb);

        if (type is ReportType.HealthReport or ReportType.FullReport)
            await AppendHealthText(sb);

        if (type is ReportType.OptimizationLog or ReportType.FullReport)
            AppendOptimizationLogText(sb);

        return sb.ToString();
    }

    private async Task AppendSnapshotText(StringBuilder sb)
    {
        HardwareInfo hw;
        IReadOnlyList<DiskHealthInfo> disks;
        try
        {
            hw    = await _hardware.GetHardwareInfoAsync();
            disks = await _diskHealth.GetDiskHealthAsync();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Error collecting hardware info: {ex.Message}]");
            return;
        }

        sb.AppendLine("====================================");
        sb.AppendLine("OPTIMIZER SYSTEM SNAPSHOT");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("====================================");
        sb.AppendLine();

        sb.AppendLine("HARDWARE");
        sb.AppendLine("--------");
        sb.AppendLine($"CPU: {hw.Cpu.Name} ({hw.Cpu.Cores} cores, {hw.Cpu.LogicalProcessors} threads, {hw.Cpu.MaxClockSpeedMHz} MHz)");
        sb.AppendLine($"Memory: {ByteFormatter.Format(hw.Memory.TotalBytes)} ({hw.Memory.SpeedMHz} MHz, {hw.Memory.ModuleCount} module(s))");
        foreach (var gpu in hw.Gpus)
            sb.AppendLine($"GPU: {gpu.Name} ({gpu.VramText} VRAM)");
        sb.AppendLine($"Motherboard: {hw.Motherboard.Manufacturer} {hw.Motherboard.Model}");
        sb.AppendLine($"BIOS: {hw.Motherboard.BiosVendor} {hw.Motherboard.BiosVersion} ({hw.Motherboard.BiosDate:yyyy-MM-dd})");
        sb.AppendLine();

        sb.AppendLine("STORAGE");
        sb.AppendLine("-------");
        foreach (var disk in disks)
        {
            var wear = disk.WearPercentage.HasValue
                ? $"{100 - disk.WearPercentage}% life remaining"
                : disk.HealthStatus;
            var temp = disk.TemperatureCelsius.HasValue ? $", {disk.TemperatureCelsius}°C" : "";
            sb.AppendLine($"- {disk.Model} ({disk.SizeText} {disk.MediaType} via {disk.BusType}): {wear}{temp}");
        }
        sb.AppendLine();

        sb.AppendLine("OS");
        sb.AppendLine("--");
        sb.AppendLine($"{hw.Os.Name} (Build {hw.Os.Build})");
        sb.AppendLine($"Architecture: {hw.Os.Architecture}");
        if (hw.Os.InstallDate != default)
            sb.AppendLine($"Installed: {hw.Os.InstallDate:yyyy-MM-dd}");
        sb.AppendLine($"Secure Boot: {(hw.Os.IsSecureBoot ? "Enabled" : "Disabled")}");
        sb.AppendLine();
    }

    private async Task AppendHealthText(StringBuilder sb)
    {
        sb.AppendLine("====================================");
        sb.AppendLine("HEALTH REPORT");
        sb.AppendLine("====================================");
        sb.AppendLine();

        IReadOnlyList<DiagnosticFinding> findings;
        try
        {
            findings = await _diagnostics.RunQuickScanAsync();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"[Error running diagnostics: {ex.Message}]");
            sb.AppendLine();
            return;
        }

        if (findings.Count == 0)
        {
            sb.AppendLine("No issues found.");
        }
        else
        {
            var criticals = findings.Where(f => f.Severity == FindingSeverity.Critical).ToList();
            var warnings  = findings.Where(f => f.Severity == FindingSeverity.Warning).ToList();
            var infos     = findings.Where(f => f.Severity == FindingSeverity.Info).ToList();

            if (criticals.Count > 0)
            {
                sb.AppendLine("CRITICAL");
                foreach (var f in criticals)
                {
                    sb.AppendLine($"  [CRITICAL] {f.Title}");
                    sb.AppendLine($"             {f.Description}");
                    if (!string.IsNullOrWhiteSpace(f.Recommendation))
                        sb.AppendLine($"             Recommendation: {f.Recommendation}");
                }
                sb.AppendLine();
            }

            if (warnings.Count > 0)
            {
                sb.AppendLine("WARNINGS");
                foreach (var f in warnings)
                {
                    sb.AppendLine($"  [WARNING] {f.Title}");
                    sb.AppendLine($"            {f.Description}");
                    if (!string.IsNullOrWhiteSpace(f.Recommendation))
                        sb.AppendLine($"            Recommendation: {f.Recommendation}");
                }
                sb.AppendLine();
            }

            if (infos.Count > 0)
            {
                sb.AppendLine("INFORMATION");
                foreach (var f in infos)
                    sb.AppendLine($"  [INFO] {f.Title}: {f.Description}");
                sb.AppendLine();
            }
        }
    }

    private void AppendOptimizationLogText(StringBuilder sb)
    {
        sb.AppendLine("====================================");
        sb.AppendLine("OPTIMIZATION LOG");
        sb.AppendLine("====================================");
        sb.AppendLine();

        var entries = _history.Entries;
        if (entries.Count == 0)
        {
            sb.AppendLine("No optimization history recorded.");
            sb.AppendLine();
            return;
        }

        var byDay = entries
            .GroupBy(e => e.TimestampUtc.ToLocalTime().Date)
            .OrderByDescending(g => g.Key);

        foreach (var day in byDay)
        {
            sb.AppendLine(day.Key.ToString("dddd, MMMM d, yyyy"));
            sb.AppendLine(new string('-', 40));
            foreach (var e in day.OrderByDescending(x => x.TimestampUtc))
            {
                var time    = e.TimestampUtc.ToLocalTime().ToString("HH:mm");
                var action  = e.Action switch
                {
                    HistoryAction.Applied  => "Applied",
                    HistoryAction.Undone   => "Undone",
                    HistoryAction.OneTime  => "Run",
                    _ => e.Action.ToString()
                };
                var undone  = e.IsUndone ? " [undone]" : "";
                sb.AppendLine($"  {time}  [{action}] {e.OptimizationTitle} ({e.Category}){undone}");
                if (!string.IsNullOrWhiteSpace(e.ResultText))
                    sb.AppendLine($"         Result: {e.ResultText}");
            }
            sb.AppendLine();
        }
    }

    // ── HTML builder ──────────────────────────────────────────────────────────

    private async Task<string> BuildHtmlAsync(ReportType type)
    {
        var body = new StringBuilder();

        if (type is ReportType.SystemSnapshot or ReportType.FullReport)
            await AppendSnapshotHtml(body);

        if (type is ReportType.HealthReport or ReportType.FullReport)
            await AppendHealthHtml(body);

        if (type is ReportType.OptimizationLog or ReportType.FullReport)
            AppendOptimizationLogHtml(body);

        var title = ReportTitle(type);
        var ts    = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>{{HtmlEncode(title)}}</title>
              <style>
                body{font-family:Segoe UI,system-ui,sans-serif;background:#0f0f0f;color:#e5e5e5;margin:0;padding:24px}
                h1{font-size:1.6rem;font-weight:700;margin-bottom:4px}
                .meta{color:#888;font-size:.85rem;margin-bottom:32px}
                h2{font-size:1.1rem;font-weight:600;text-transform:uppercase;letter-spacing:.08em;
                   color:#9ca3af;border-bottom:1px solid #2d2d2d;padding-bottom:8px;margin-top:32px}
                table{border-collapse:collapse;width:100%;margin-bottom:16px}
                th{text-align:left;font-size:.78rem;text-transform:uppercase;letter-spacing:.06em;
                   color:#9ca3af;padding:6px 12px;border-bottom:1px solid #2d2d2d}
                td{padding:8px 12px;border-bottom:1px solid #1c1c1c;font-size:.9rem}
                tr:hover td{background:#1a1a1a}
                .badge{display:inline-block;padding:2px 8px;border-radius:4px;font-size:.75rem;font-weight:600}
                .critical{background:#7f1d1d;color:#fca5a5}
                .warning{background:#78350f;color:#fde68a}
                .info{background:#1e3a5f;color:#93c5fd}
                .ok{background:#14532d;color:#86efac}
              </style>
            </head>
            <body>
              <h1>{{HtmlEncode(title)}}</h1>
              <div class="meta">Generated {{HtmlEncode(ts)}}</div>
              {{body}}
            </body>
            </html>
            """;
    }

    private async Task AppendSnapshotHtml(StringBuilder sb)
    {
        sb.AppendLine("<h2>Hardware</h2>");

        HardwareInfo hw;
        IReadOnlyList<DiskHealthInfo> disks;
        try
        {
            hw    = await _hardware.GetHardwareInfoAsync();
            disks = await _diskHealth.GetDiskHealthAsync();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"<p style='color:#f87171'>Error collecting hardware info: {HtmlEncode(ex.Message)}</p>");
            return;
        }

        sb.AppendLine("<table><thead><tr><th>Component</th><th>Details</th></tr></thead><tbody>");
        Row(sb, "CPU", $"{hw.Cpu.Name} &mdash; {hw.Cpu.Cores}C / {hw.Cpu.LogicalProcessors}T &nbsp; {hw.Cpu.MaxClockSpeedMHz} MHz");
        Row(sb, "Memory", $"{ByteFormatter.Format(hw.Memory.TotalBytes)} &nbsp; {hw.Memory.SpeedMHz} MHz &nbsp; {hw.Memory.ModuleCount} module(s)");
        foreach (var gpu in hw.Gpus)
            Row(sb, "GPU", $"{HtmlEncode(gpu.Name)} &nbsp; {gpu.VramText} VRAM");
        Row(sb, "Motherboard", $"{HtmlEncode(hw.Motherboard.Manufacturer)} {HtmlEncode(hw.Motherboard.Model)}");
        Row(sb, "BIOS", $"{HtmlEncode(hw.Motherboard.BiosVendor)} {HtmlEncode(hw.Motherboard.BiosVersion)} &nbsp; ({hw.Motherboard.BiosDate:yyyy-MM-dd})");
        sb.AppendLine("</tbody></table>");

        sb.AppendLine("<h2>OS</h2>");
        sb.AppendLine("<table><thead><tr><th>Item</th><th>Value</th></tr></thead><tbody>");
        Row(sb, "OS", HtmlEncode(hw.Os.Name));
        Row(sb, "Build", HtmlEncode(hw.Os.Build));
        Row(sb, "Architecture", HtmlEncode(hw.Os.Architecture));
        Row(sb, "Installed", hw.Os.InstallDate != default ? hw.Os.InstallDate.ToString("yyyy-MM-dd") : "—");
        Row(sb, "Secure Boot", hw.Os.IsSecureBoot ? "<span class='badge ok'>Enabled</span>" : "<span class='badge critical'>Disabled</span>");
        sb.AppendLine("</tbody></table>");

        if (disks.Count > 0)
        {
            sb.AppendLine("<h2>Storage</h2>");
            sb.AppendLine("<table><thead><tr><th>Drive</th><th>Type</th><th>Size</th><th>Health</th><th>Temp</th></tr></thead><tbody>");
            foreach (var d in disks)
            {
                var badge = d.HealthBadge switch
                {
                    "Good"     => "ok",
                    "Warning"  => "warning",
                    "Critical" => "critical",
                    _          => "info"
                };
                sb.AppendLine($"<tr><td>{HtmlEncode(d.Model)}</td><td>{HtmlEncode(d.MediaType)} / {HtmlEncode(d.BusType)}</td>" +
                              $"<td>{HtmlEncode(d.SizeText)}</td>" +
                              $"<td><span class='badge {badge}'>{HtmlEncode(d.HealthBadge)}</span></td>" +
                              $"<td>{HtmlEncode(d.TemperatureText)}</td></tr>");
            }
            sb.AppendLine("</tbody></table>");
        }
    }

    private async Task AppendHealthHtml(StringBuilder sb)
    {
        sb.AppendLine("<h2>Health Report</h2>");

        IReadOnlyList<DiagnosticFinding> findings;
        try
        {
            findings = await _diagnostics.RunQuickScanAsync();
        }
        catch (Exception ex)
        {
            sb.AppendLine($"<p style='color:#f87171'>Error running diagnostics: {HtmlEncode(ex.Message)}</p>");
            return;
        }

        if (findings.Count == 0)
        {
            sb.AppendLine("<p style='color:#86efac'>No issues found &mdash; system looks healthy.</p>");
            return;
        }

        sb.AppendLine("<table><thead><tr><th>Severity</th><th>Title</th><th>Description</th><th>Recommendation</th></tr></thead><tbody>");
        foreach (var f in findings.OrderBy(x => x.Severity))
        {
            var badge = f.Severity switch
            {
                FindingSeverity.Critical => "critical",
                FindingSeverity.Warning  => "warning",
                _                        => "info"
            };
            sb.AppendLine($"<tr>" +
                          $"<td><span class='badge {badge}'>{HtmlEncode(f.SeverityBadge)}</span></td>" +
                          $"<td>{HtmlEncode(f.Title)}</td>" +
                          $"<td>{HtmlEncode(f.Description)}</td>" +
                          $"<td>{HtmlEncode(f.Recommendation)}</td>" +
                          $"</tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private void AppendOptimizationLogHtml(StringBuilder sb)
    {
        sb.AppendLine("<h2>Optimization Log</h2>");
        var entries = _history.Entries;

        if (entries.Count == 0)
        {
            sb.AppendLine("<p style='color:#9ca3af'>No optimization history recorded.</p>");
            return;
        }

        sb.AppendLine("<table><thead><tr><th>Date/Time</th><th>Action</th><th>Optimization</th><th>Category</th><th>Result</th></tr></thead><tbody>");
        foreach (var e in entries.OrderByDescending(x => x.TimestampUtc))
        {
            var ts = e.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var action = e.Action switch
            {
                HistoryAction.Applied => "<span class='badge ok'>Applied</span>",
                HistoryAction.Undone  => "<span class='badge warning'>Undone</span>",
                HistoryAction.OneTime => "<span class='badge info'>Run</span>",
                _ => HtmlEncode(e.Action.ToString())
            };
            var undone = e.IsUndone ? " <span class='badge warning'>undone</span>" : "";
            sb.AppendLine($"<tr><td>{ts}</td><td>{action}</td>" +
                          $"<td>{HtmlEncode(e.OptimizationTitle)}{undone}</td>" +
                          $"<td>{HtmlEncode(e.Category)}</td>" +
                          $"<td>{HtmlEncode(e.ResultText ?? "")}</td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    // ── JSON builder ──────────────────────────────────────────────────────────

    private async Task<string> BuildJsonAsync(ReportType type)
    {
        var obj = new Dictionary<string, object>
        {
            ["generated"] = DateTime.Now.ToString("o"),
            ["reportType"] = type.ToString()
        };

        if (type is ReportType.SystemSnapshot or ReportType.FullReport)
        {
            try
            {
                var hw    = await _hardware.GetHardwareInfoAsync();
                var disks = await _diskHealth.GetDiskHealthAsync();
                obj["hardware"] = hw;
                obj["storage"]  = disks;
            }
            catch (Exception ex)
            {
                obj["hardwareError"] = ex.Message;
            }
        }

        if (type is ReportType.HealthReport or ReportType.FullReport)
        {
            try
            {
                obj["diagnostics"] = await _diagnostics.RunQuickScanAsync();
            }
            catch (Exception ex)
            {
                obj["diagnosticsError"] = ex.Message;
            }
        }

        if (type is ReportType.OptimizationLog or ReportType.FullReport)
        {
            obj["optimizationLog"] = _history.Entries.Select(e => new
            {
                e.OptimizationId,
                e.OptimizationTitle,
                e.Category,
                timestamp   = e.TimestampUtc.ToString("o"),
                action      = e.Action.ToString(),
                e.IsReversible,
                e.IsUndone,
                e.ResultText
            });
        }

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    // ── Small helpers ─────────────────────────────────────────────────────────

    private static void Row(StringBuilder sb, string label, string value)
        => sb.AppendLine($"<tr><td style='color:#9ca3af;width:160px'>{label}</td><td>{value}</td></tr>");

    private static string HtmlEncode(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }
}
