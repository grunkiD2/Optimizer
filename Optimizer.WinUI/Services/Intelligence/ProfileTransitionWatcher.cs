using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// Records fgwatch profile transitions into ProfileTimeline. Poll IFancontrolStatusService on a timer
/// (or call <see cref="TickAsync"/> from an existing 5 s loop); when LastAppliedProfile changes, close the
/// open interval (set EndTs = now) and open a new one. Append-only; the engine is never written. Idempotent
/// on no-change. The clock is injectable for testability.
///
/// <para><see cref="_lastSeen"/> is rehydrated from the open ProfileTimeline row on the FIRST tick after
/// (re)launch — the watcher is a DI singleton, so without this an app restart would see the (genuinely still
/// active) profile as a change and SPLIT its live interval, fragmenting the outcomes rollup. After rehydrate,
/// a restart with the same profile active is a no-op; a restart with a different profile is a single genuine
/// transition.</para>
/// </summary>
public sealed class ProfileTransitionWatcher
{
    // ≈1 min at 5 s telemetry: don't persist an outcome for a trivial sub-minute interval (the coolant
    // hasn't settled and a p95 over <12 samples is noise). Genuine transitions below this still close the
    // timeline interval normally — only the best-effort ProfileOutcomes rollup is skipped.
    private const int MinOutcomeSamples = 12;

    private readonly IFancontrolStatusService _status;
    private readonly IProfileOutcomesService _outcomes;
    private readonly Func<SqliteConnection> _connFactory;
    private readonly Func<string> _nowUtcIso;   // injectable clock (DateTimeOffset.UtcNow.ToString("o") in prod)
    private string? _lastSeen;
    private bool _rehydrated;
    private int _ticking;   // 0/1 re-entrancy guard: TickAsync is fire-and-forget off a 5 s UI timer.

    public ProfileTransitionWatcher(IFancontrolStatusService status, IProfileOutcomesService outcomes, Func<SqliteConnection> connectionFactory, Func<string>? nowUtcIso = null)
    {
        _status = status;
        _outcomes = outcomes;
        _connFactory = connectionFactory;
        _nowUtcIso = nowUtcIso ?? (() => DateTimeOffset.UtcNow.ToString("o"));
    }

    /// <summary>Call on each status refresh (~5 s). Writes a transition only when the profile actually changes.</summary>
    public async Task TickAsync()
    {
        // Cheap re-entrancy guard: two overlapping ticks must not both run the first-tick/transition path.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            string? current;
            try { current = _status.GetStatus()?.Profiles?.LastAppliedProfile; }
            catch { return; }                       // fail-safe: never let the watcher throw into the UI loop

            // First tick after (re)launch: ADOPT the currently-open interval instead of splitting it.
            // Without this, a restart with the same profile still active would look like a change below.
            if (!_rehydrated)
            {
                try
                {
                    await using var rc = _connFactory();
                    await rc.OpenAsync();
                    await using var rcmd = rc.CreateCommand();
                    rcmd.CommandText = "SELECT ProfileName FROM ProfileTimeline WHERE EndTs IS NULL ORDER BY StartTs DESC LIMIT 1";
                    var open = await rcmd.ExecuteScalarAsync();
                    if (open is string s && !string.IsNullOrEmpty(s)) _lastSeen = s;
                    _rehydrated = true;
                }
                catch { /* transient DB issue → leave _rehydrated=false so we retry adopting next tick */ }
            }

            if (string.IsNullOrEmpty(current) || current == _lastSeen) return;

            var now = _nowUtcIso();
            try
            {
                await using var conn = _connFactory();
                await conn.OpenAsync();

                // Capture the interval we're ABOUT to close so we can roll it up after the commit.
                // (closedProfile null = no prior open interval, e.g. first-ever transition → nothing to roll up.)
                string? closedProfile = null, closedStart = null;
                await using (var pick = conn.CreateCommand())
                {
                    pick.CommandText = "SELECT ProfileName, StartTs FROM ProfileTimeline WHERE EndTs IS NULL ORDER BY StartTs DESC LIMIT 1";
                    await using var pr = await pick.ExecuteReaderAsync();
                    if (await pr.ReadAsync()) { closedProfile = pr.GetString(0); closedStart = pr.GetString(1); }
                }

                // Close-then-open must be atomic: a crash between them would leave a gap in the timeline.
                await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
                await using (var close = conn.CreateCommand())
                {
                    close.Transaction = tx;
                    close.CommandText = "UPDATE ProfileTimeline SET EndTs = $t WHERE EndTs IS NULL";
                    close.Parameters.AddWithValue("$t", now);
                    await close.ExecuteNonQueryAsync();
                }
                await using (var open = conn.CreateCommand())
                {
                    open.Transaction = tx;
                    open.CommandText = "INSERT INTO ProfileTimeline (ProfileName, StartTs, EndTs, Exe) VALUES ($p, $s, NULL, $e)";
                    open.Parameters.AddWithValue("$p", current);
                    open.Parameters.AddWithValue("$s", now);
                    string? fgExe = null;
                    try { fgExe = _status.GetStatus()?.Profiles?.ForegroundExe; } catch { }
                    open.Parameters.AddWithValue("$e", (object?)fgExe ?? DBNull.Value);
                    await open.ExecuteNonQueryAsync();
                }
                await tx.CommitAsync();
                _lastSeen = current;

                // Close the verification loop: roll up + SAVE the just-closed interval so the editor's
                // "Verificerer…" band can show a coolant-p95 outcome. Best-effort by construction — runs
                // AFTER the commit (rollup reads committed telemetry) and in its OWN try/catch, so a rollup
                // failure NEVER breaks the transition (the timeline write already succeeded) or throws into
                // the UI loop. Only on a GENUINE close of a real prior interval — never the rehydrate path.
                if (closedProfile is not null && closedStart is not null)
                {
                    try
                    {
                        var rec = await _outcomes.RollupIntervalAsync(closedProfile, closedStart, now);
                        if (rec is not null && rec.SampleCount >= MinOutcomeSamples)
                            await _outcomes.SaveAsync(rec);
                    }
                    catch { /* outcome rollup is best-effort: never break the transition */ }
                }
            }
            catch { /* transient DB lock → retry next tick; _lastSeen unchanged so we re-attempt */ }
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }
}
