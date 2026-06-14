using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Optimizer.WinUI.Services.Intelligence;

/// <summary>
/// The verification loop's backbone (Profil 2.0 §6): rolls up FancontrolTelemetry over a profile's
/// active interval into a coolant-p95 (+ PresentMon fps-1%-low joined by caller) and exposes
/// "last vs previous". SQLite has no PERCENTILE_CONT, so p95 is an ORDER BY / OFFSET pick.
/// Pure DB access via an injected connection factory → unit-testable on a temp DB.
/// </summary>
public sealed class ProfileOutcomesService : IProfileOutcomesService
{
    private readonly Func<SqliteConnection> _connFactory;

    public ProfileOutcomesService(Func<SqliteConnection> connectionFactory) => _connFactory = connectionFactory;

    public async Task<OutcomeRecord?> RollupIntervalAsync(string profileName, string startTs, string endTs)
    {
        await using var conn = _connFactory();
        await conn.OpenAsync();

        // sample count over the interval [startTs, endTs)
        int n;
        await using (var cnt = conn.CreateCommand())
        {
            cnt.CommandText = "SELECT COUNT(*) FROM FancontrolTelemetry WHERE Ts >= $a AND Ts < $b";
            cnt.Parameters.AddWithValue("$a", startTs);
            cnt.Parameters.AddWithValue("$b", endTs);
            n = Convert.ToInt32(await cnt.ExecuteScalarAsync());
        }

        double? p95 = null;
        if (n >= 5)
        {
            // SQLite has no PERCENTILE_CONT: pick the p95 row by ORDER BY ASC + OFFSET.
            int offset = (int)(n * 0.95);
            if (offset >= n) offset = n - 1;
            await using var q = conn.CreateCommand();
            q.CommandText = "SELECT Coolant FROM FancontrolTelemetry WHERE Ts >= $a AND Ts < $b AND Coolant IS NOT NULL ORDER BY Coolant ASC LIMIT 1 OFFSET $o";
            q.Parameters.AddWithValue("$a", startTs);
            q.Parameters.AddWithValue("$b", endTs);
            q.Parameters.AddWithValue("$o", offset);
            var v = await q.ExecuteScalarAsync();
            if (v is not null && v is not DBNull) p95 = Convert.ToDouble(v);
        }

        int durMin = DurationMinutes(startTs, endTs);
        // GpuFps1Low: PresentMon join is a later concern — leave it null, never fabricated.
        return new OutcomeRecord(profileName, endTs, durMin, n, p95, GpuFps1Low: null);
    }

    public async Task SaveAsync(OutcomeRecord rec)
    {
        await using var conn = _connFactory();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ProfileOutcomes (ProfileName, RecordedAtUtc, DurationMinutes, SampleCount, CoolantP95, GpuFps1Low)
                            VALUES ($p, $r, $d, $s, $c, $f)";
        cmd.Parameters.AddWithValue("$p", rec.ProfileName);
        cmd.Parameters.AddWithValue("$r", rec.RecordedAtUtc);
        cmd.Parameters.AddWithValue("$d", rec.DurationMinutes);
        cmd.Parameters.AddWithValue("$s", rec.SampleCount);
        cmd.Parameters.AddWithValue("$c", (object?)rec.CoolantP95 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", (object?)rec.GpuFps1Low ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(OutcomeRecord? Latest, OutcomeRecord? Previous)> LastVsPreviousAsync(string profileName)
    {
        await using var conn = _connFactory();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ProfileName, RecordedAtUtc, DurationMinutes, SampleCount, CoolantP95, GpuFps1Low
                            FROM ProfileOutcomes WHERE ProfileName = $p ORDER BY RecordedAtUtc DESC LIMIT 2";
        cmd.Parameters.AddWithValue("$p", profileName);
        var recs = new System.Collections.Generic.List<OutcomeRecord>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
            recs.Add(new OutcomeRecord(
                rdr.GetString(0), rdr.GetString(1), rdr.GetInt32(2), rdr.GetInt32(3),
                rdr.IsDBNull(4) ? null : rdr.GetDouble(4),
                rdr.IsDBNull(5) ? null : rdr.GetDouble(5)));
        return (recs.Count > 0 ? recs[0] : null, recs.Count > 1 ? recs[1] : null);
    }

    private static int DurationMinutes(string startTs, string endTs)
    {
        if (DateTimeOffset.TryParse(startTs, null, System.Globalization.DateTimeStyles.RoundtripKind, out var a) &&
            DateTimeOffset.TryParse(endTs, null, System.Globalization.DateTimeStyles.RoundtripKind, out var b))
            return (int)Math.Max(0, (b - a).TotalMinutes);
        return 0;
    }
}
