using System.Diagnostics;
using System.Text.Json;

namespace Optimizer.WinUI.Services;

public record CtlResult(bool Success, string Output);

public interface IFancontrolCommandService
{
    /// <summary>True when the federation is configured AND engine\ctl.ps1 exists.</summary>
    bool IsConfigured { get; }

    /// <summary>Profile names from profiles\profiles.json (empty when unreadable).</summary>
    IReadOnlyList<string> GetProfileNames();

    Task<CtlResult> ApplyProfileAsync(string profileName, CancellationToken ct = default);

    /// <summary>mode must be one of on/off/auto.</summary>
    Task<CtlResult> SetNightAsync(string mode, CancellationToken ct = default);

    Task<CtlResult> AckAlertsAsync(string? note, CancellationToken ct = default);

    /// <summary>Etape 1 alarm response: controlled FanBrain restart with fresh-state verification.</summary>
    Task<CtlResult> RestartBrainAsync(CancellationToken ct = default);

    /// <summary>Cooperative fgwatch restart (stop-flag → Ready → start), verified by fresh state.</summary>
    Task<CtlResult> RestartFgwatchAsync(CancellationToken ct = default);
}

/// <summary>
/// The Fancontrol command bridge: every mutation goes through the Fancontrol system's own
/// engine\ctl.ps1 (its UI contract) — Optimizer NEVER writes Fancontrol state directly
/// (docs/MACHINE-OWNERSHIP.md; profile_hint.txt alone carries 4 contracts). Only a fixed,
/// validated command set is exposed: apply-profile (validated against profiles.json),
/// night on|off|auto, and ack-alerts.
/// </summary>
public class FancontrolCommandService : IFancontrolCommandService
{
    private static readonly string[] NightModes = ["on", "off", "auto"];
    private static readonly TimeSpan CtlTimeout = TimeSpan.FromSeconds(45); // apply-profile drives DDC/HDR and can take ~20 s

    // powershell.exe writes redirected stdout in the OEM codepage. .NET Core does not ship OEM
    // encodings by default — they need CodePagesEncodingProvider, and Encoding.GetEncoding throws
    // without it (live-found: an inline initializer 500'ed every command endpoint). Null → default
    // decoding; only Danish prose would garble, the ASCII contract line never does.
    private static readonly System.Text.Encoding? OemEncoding = ResolveOemEncoding();

    private static System.Text.Encoding? ResolveOemEncoding()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch { return null; }
    }

    private readonly string _root;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<CtlResult>>? _runnerOverride;

    /// <param name="stateDir">AppSettings.FancontrolStateDir (…\state); the engine root is its parent.</param>
    /// <param name="runnerOverride">Test seam — replaces the real powershell.exe invocation.</param>
    public FancontrolCommandService(string stateDir, Func<IReadOnlyList<string>, CancellationToken, Task<CtlResult>>? runnerOverride = null)
    {
        var dir = stateDir?.Trim().TrimEnd('\\', '/') ?? "";
        _root = dir.Length == 0 ? "" : Path.GetDirectoryName(dir) ?? "";
        _runnerOverride = runnerOverride;
    }

    private string CtlPath => Path.Combine(_root, "engine", "ctl.ps1");

    public bool IsConfigured => _root.Length > 0 && (_runnerOverride != null || File.Exists(CtlPath));

    public IReadOnlyList<string> GetProfileNames()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "profiles", "profiles.json")));
            if (doc.RootElement.TryGetProperty("profiles", out var p) && p.ValueKind == JsonValueKind.Object)
                return p.EnumerateObject().Select(o => o.Name).ToList();
        }
        catch { /* unreadable → empty; ApplyProfileAsync then refuses (fail-closed) */ }
        return [];
    }

    public Task<CtlResult> ApplyProfileAsync(string profileName, CancellationToken ct = default)
    {
        var requested = (profileName ?? "").Trim();
        // "Navn+modul" syntax is allowed — validate the base profile name (fail-closed:
        // an unreadable profiles.json refuses every name rather than passing anything through).
        var baseName = requested.Split('+')[0].Trim();
        var known = GetProfileNames();
        if (baseName.Length == 0 || !known.Contains(baseName, StringComparer.OrdinalIgnoreCase))
            return Task.FromResult(new CtlResult(false,
                $"Unknown profile '{requested}'. Known profiles: {(known.Count > 0 ? string.Join(", ", known) : "(profiles.json unreadable)")}"));
        return RunCtlAsync(["apply-profile", requested], ct);
    }

    public Task<CtlResult> SetNightAsync(string mode, CancellationToken ct = default)
    {
        var m = (mode ?? "").Trim().ToLowerInvariant();
        if (!NightModes.Contains(m))
            return Task.FromResult(new CtlResult(false, $"Invalid night mode '{mode}' — use on, off or auto."));
        return RunCtlAsync(["night", m], ct);
    }

    public Task<CtlResult> AckAlertsAsync(string? note, CancellationToken ct = default)
    {
        // The note travels into events.jsonl — keep it single-line and bounded.
        var clean = new string((note ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length > 200) clean = clean[..200];
        return clean.Length > 0 ? RunCtlAsync(["ack-alerts", clean], ct) : RunCtlAsync(["ack-alerts"], ct);
    }

    // The restart commands verify fresh daemon state before answering — restart-fgwatch's worst
    // case is ~71 s (45 s cooperative stop + 6 s grace + 20 s fresh-state wait), restart-brain's
    // ~35 s. The default 45 s ctl timeout would kill them mid-verification.
    public Task<CtlResult> RestartBrainAsync(CancellationToken ct = default)
        => RunCtlAsync(["restart-brain"], ct, TimeSpan.FromSeconds(90));

    public Task<CtlResult> RestartFgwatchAsync(CancellationToken ct = default)
        => RunCtlAsync(["restart-fgwatch"], ct, TimeSpan.FromSeconds(120));

    /// <summary>
    /// R1 result contract (2026-06-13): ctl.ps1's LAST stdout line is always compact JSON
    /// {ok,cmd,msg} and the exit code is real (0 ok / 1 fail). Parsed fail-closed: a missing or
    /// garbled contract line counts as failure — the success flag must never be decorative again
    /// (pre-R1 the engine never set exit codes, so ExitCode==0 reported success on every failure).
    /// Only stdout is scanned (PowerShell writes its own error records to stderr).
    /// </summary>
    public static CtlResult ParseCtlResult(string stdout, string stderr, int exitCode)
    {
        var lines = (stdout ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = lines.Length > 0 ? lines[^1] : "";
        if (last.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(last);
                var root = doc.RootElement;
                if (root.TryGetProperty("ok", out var okEl) &&
                    (okEl.ValueKind == JsonValueKind.True || okEl.ValueKind == JsonValueKind.False))
                {
                    var ok = okEl.GetBoolean();
                    var msg = root.TryGetProperty("msg", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                        ? (msgEl.GetString() ?? "").Trim() : "";
                    // ok:true with a non-zero exit code is contradictory (process died after
                    // printing?) — fail closed on the conjunction.
                    var success = ok && exitCode == 0;
                    return new CtlResult(success, msg.Length > 0 ? msg : RawOutput(stdout, stderr, exitCode));
                }
            }
            catch (JsonException) { /* fall through to fail-closed below */ }
        }
        return new CtlResult(false, $"(no R1 JSON result contract from ctl, exit {exitCode}) {RawOutput(stdout, stderr, exitCode)}");

        static string RawOutput(string stdout, string stderr, int exitCode)
        {
            var combined = $"{stdout?.Trim()}\n{stderr?.Trim()}".Trim();
            return combined.Length > 0 ? combined : $"(exit {exitCode})";
        }
    }

    private async Task<CtlResult> RunCtlAsync(IReadOnlyList<string> commandAndArg, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (!IsConfigured) return new CtlResult(false, "Fancontrol federation not configured (AppSettings.FancontrolStateDir).");
        if (_runnerOverride != null) return await _runnerOverride(commandAndArg, ct);
        var effectiveTimeout = timeout ?? CtlTimeout;

        // Windows PowerShell 5.1 — the engine targets it explicitly. ArgumentList quotes each
        // token (multi-token -Args died silently in param binding when unquoted — known trap).
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // OEM decoding so Danish prose survives (R1 review find); the JSON contract line
            // itself is pure ASCII (\uXXXX-escaped by the engine) and never depends on this.
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", CtlPath, "-Command", commandAndArg[0] })
            psi.ArgumentList.Add(a);
        if (commandAndArg.Count > 1)
        {
            psi.ArgumentList.Add("-Args");
            psi.ArgumentList.Add(commandAndArg[1]);
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return new CtlResult(false, "Could not start powershell.exe.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(effectiveTimeout);
            var stdout = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderr = proc.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                EngineLog.Write($"[Fancontrol] ctl {commandAndArg[0]} timed out after {effectiveTimeout.TotalSeconds:F0} s");
                return new CtlResult(false, $"ctl.ps1 {commandAndArg[0]} timed out.");
            }
            var result = ParseCtlResult(await stdout, await stderr, proc.ExitCode);
            EngineLog.Write($"[Fancontrol] ctl {string.Join(' ', commandAndArg)} → exit {proc.ExitCode}, ok={result.Success}: {result.Output}");
            return result;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"[Fancontrol] ctl {commandAndArg[0]} failed", ex);
            return new CtlResult(false, ex.Message);
        }
    }
}
