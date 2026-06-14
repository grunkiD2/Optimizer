using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// Records fgwatch profile transitions into ProfileTimeline. Poll IFancontrolStatusService on a timer
/// (or call <see cref="TickAsync"/> from an existing 5 s loop); when LastAppliedProfile changes, close the
/// open interval (set EndTs = now) and open a new one. Append-only; the engine is never written. Idempotent
/// on no-change. The clock is injectable for testability.
/// </summary>
public sealed class ProfileTransitionWatcher
{
    private readonly IFancontrolStatusService _status;
    private readonly Func<SqliteConnection> _connFactory;
    private readonly Func<string> _nowUtcIso;   // injectable clock (DateTimeOffset.UtcNow.ToString("o") in prod)
    private string? _lastSeen;

    public ProfileTransitionWatcher(IFancontrolStatusService status, Func<SqliteConnection> connectionFactory, Func<string>? nowUtcIso = null)
    {
        _status = status;
        _connFactory = connectionFactory;
        _nowUtcIso = nowUtcIso ?? (() => DateTimeOffset.UtcNow.ToString("o"));
    }

    /// <summary>Call on each status refresh (~5 s). Writes a transition only when the profile actually changes.</summary>
    public async Task TickAsync()
    {
        string? current;
        try { current = _status.GetStatus()?.Profiles?.LastAppliedProfile; }
        catch { return; }                       // fail-safe: never let the watcher throw into the UI loop
        if (string.IsNullOrEmpty(current) || current == _lastSeen) return;

        var now = _nowUtcIso();
        try
        {
            await using var conn = _connFactory();
            await conn.OpenAsync();
            await using (var close = conn.CreateCommand())
            {
                close.CommandText = "UPDATE ProfileTimeline SET EndTs = $t WHERE EndTs IS NULL";
                close.Parameters.AddWithValue("$t", now);
                await close.ExecuteNonQueryAsync();
            }
            await using var open = conn.CreateCommand();
            open.CommandText = "INSERT INTO ProfileTimeline (ProfileName, StartTs, EndTs, Exe) VALUES ($p, $s, NULL, $e)";
            open.Parameters.AddWithValue("$p", current);
            open.Parameters.AddWithValue("$s", now);
            string? fgExe = null;
            try { fgExe = _status.GetStatus()?.Profiles?.ForegroundExe; } catch { }
            open.Parameters.AddWithValue("$e", (object?)fgExe ?? DBNull.Value);
            await open.ExecuteNonQueryAsync();
            _lastSeen = current;
        }
        catch { /* transient DB lock → retry next tick; _lastSeen unchanged so we re-attempt */ }
    }
}
