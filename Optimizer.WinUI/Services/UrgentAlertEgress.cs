using System.Diagnostics;

namespace Optimizer.WinUI.Services;

/// <summary>
/// R5 alarm-egress policy: URGENT findings must reach the user's PHONE, not die as a toast
/// from a tray-minimized app. Urgent → ntfy push through the Fancontrol federation's own
/// engine\notify.ps1 (Fancontrol owns the ntfy channel + topic secret — docs/MACHINE-OWNERSHIP.md);
/// informational stays in the Optimizer UI. Callers keep their toast/event behavior — this is an
/// ADDITIONAL egress, and it degrades silently to "false" when the federation is unconfigured.
/// </summary>
public interface IUrgentAlertEgress
{
    /// <summary>True when the federation is configured AND engine\notify.ps1 exists.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Push an urgent finding to the phone. Returns false (without throwing) when unconfigured,
    /// deduplicated by the cooldown, or the push fails — urgency must never crash the caller.
    /// </summary>
    Task<bool> PushUrgentAsync(string title, string message, CancellationToken ct = default);
}

public class UrgentAlertEgress : IUrgentAlertEgress
{
    // One urgent push per title per cooldown window — hourly evaluators must not re-push the
    // same dying disk all day. (The sentinel's own pushes dedup the same way, digit-stripped.)
    private static readonly TimeSpan Cooldown = TimeSpan.FromHours(4);

    // notify.ps1's documented DNS-hang trap is ~15 s — give the push room, then give up.
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root;
    private readonly Func<string, string, CancellationToken, Task<bool>>? _pusherOverride;
    private readonly Dictionary<string, DateTimeOffset> _lastPush = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <param name="stateDir">AppSettings.FancontrolStateDir (…\state); the engine root is its parent.</param>
    /// <param name="pusherOverride">Test seam — replaces the real powershell.exe invocation.</param>
    public UrgentAlertEgress(string stateDir, Func<string, string, CancellationToken, Task<bool>>? pusherOverride = null)
    {
        var dir = stateDir?.Trim().TrimEnd('\\', '/') ?? "";
        _root = dir.Length == 0 ? "" : Path.GetDirectoryName(dir) ?? "";
        _pusherOverride = pusherOverride;
    }

    private string NotifyPath => Path.Combine(_root, "engine", "notify.ps1");

    public bool IsConfigured => _root.Length > 0 && (_pusherOverride != null || File.Exists(NotifyPath));

    public async Task<bool> PushUrgentAsync(string title, string message, CancellationToken ct = default)
    {
        if (!IsConfigured) return false;

        lock (_lock)
        {
            if (_lastPush.TryGetValue(title, out var last) && DateTimeOffset.Now - last < Cooldown)
                return false;
            _lastPush[title] = DateTimeOffset.Now;
        }

        try
        {
            var ok = _pusherOverride != null
                ? await _pusherOverride(title, message, ct)
                : await RunNotifyAsync(title, message, ct);
            EngineLog.Write($"[AlertEgress] urgent ntfy push '{title}' → {(ok ? "sent" : "FAILED")}");
            return ok;
        }
        catch (Exception ex)
        {
            EngineLog.Error($"[AlertEgress] urgent ntfy push '{title}' failed", ex);
            return false;
        }
    }

    private async Task<bool> RunNotifyAsync(string title, string message, CancellationToken ct)
    {
        // notify.ps1 is a dot-source module exposing Send-Ntfy <title> <body> <priority>.
        // Single-quote PS literals with '' doubling — title/message must never reach the
        // parser as code (same injection posture as the ctl bridge).
        static string Q(string s) => "'" + (s ?? "").Replace("'", "''").Replace("\r", " ").Replace("\n", " ") + "'";
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command",
            $". {Q(NotifyPath)}; if(Send-Ntfy {Q(title)} {Q(message)} 'urgent'){{ exit 0 }} else {{ exit 1 }}" })
            psi.ArgumentList.Add(a);

        using var proc = Process.Start(psi);
        if (proc == null) return false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PushTimeout);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            return false;
        }
    }
}
