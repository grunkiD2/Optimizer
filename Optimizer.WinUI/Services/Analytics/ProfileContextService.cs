using Optimizer.WinUI.Services.Data;

namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Records profile applications per context and resolves their success once a
/// settling window passes. "Success" = the profile was not superseded by another
/// apply within the window (i.e. the user kept it).
/// </summary>
public class ProfileContextService(DatabaseService db) : IProfileContextService
{
    public async Task<long> RecordApplicationAsync(string profileId, string context)
    {
        const string sql = """
            INSERT INTO ProfileApplications (ProfileId, Context, AppliedAtUtc, Resolved)
            VALUES (@profileId, @context, @appliedAt, 0);
            SELECT last_insert_rowid();
            """;

        var parameters = new Dictionary<string, object>
        {
            ["profileId"] = profileId,
            ["context"] = context,
            ["appliedAt"] = DateTime.UtcNow.ToString("O")
        };

        // ExecuteScalar runs the whole batch and returns the last SELECT.
        var id = await db.ExecuteScalarAsync<long>(sql, parameters);

        // Bump the apply counter on the association immediately.
        await UpsertAssociationAsync(profileId, context, applyDelta: 1, successDelta: 0);
        return id;
    }

    public async Task ResolvePendingAsync(TimeSpan settlingWindow)
    {
        var cutoff = DateTime.UtcNow - settlingWindow;

        // Pull unresolved applications older than the settling window.
        const string pendingSql = """
            SELECT Id, ProfileId, Context, AppliedAtUtc
            FROM ProfileApplications
            WHERE Resolved = 0 AND AppliedAtUtc <= @cutoff
            ORDER BY AppliedAtUtc ASC
            """;

        var pending = await db.ExecuteQueryAsync(pendingSql,
            new Dictionary<string, object> { ["cutoff"] = cutoff.ToString("O") });

        foreach (var row in pending)
        {
            var id = Convert.ToInt64(row["Id"]);
            var profileId = row["ProfileId"].ToString()!;
            var context = row["Context"].ToString()!;
            var appliedAt = DateTime.Parse(row["AppliedAtUtc"].ToString()!);

            // Was this application superseded by a *later* apply (any profile)?
            const string supersededSql = """
                SELECT COUNT(*) FROM ProfileApplications
                WHERE AppliedAtUtc > @appliedAt
                """;
            var laterCount = await db.ExecuteScalarAsync<long>(supersededSql,
                new Dictionary<string, object> { ["appliedAt"] = appliedAt.ToString("O") });

            // Within the settling window, no later apply ⇒ the user kept it ⇒ success.
            // Determine "kept" relative to the window end, not just any later apply.
            var windowEnd = appliedAt + settlingWindow;
            const string switchedInWindowSql = """
                SELECT COUNT(*) FROM ProfileApplications
                WHERE AppliedAtUtc > @appliedAt AND AppliedAtUtc <= @windowEnd
                """;
            var switchedInWindow = await db.ExecuteScalarAsync<long>(switchedInWindowSql,
                new Dictionary<string, object>
                {
                    ["appliedAt"] = appliedAt.ToString("O"),
                    ["windowEnd"] = windowEnd.ToString("O")
                });

            var wasSuccess = switchedInWindow == 0;

            // Mark resolved.
            await db.ExecuteNonQueryAsync(
                "UPDATE ProfileApplications SET Resolved = 1, WasSuccess = @s WHERE Id = @id",
                new Dictionary<string, object> { ["s"] = wasSuccess ? 1 : 0, ["id"] = id });

            if (wasSuccess)
                await UpsertAssociationAsync(profileId, context, applyDelta: 0, successDelta: 1);

            _ = laterCount; // (kept for clarity; not used directly in the verdict)
        }
    }

    public async Task<ProfileContextAssociation?> GetAssociationAsync(string profileId, string context)
    {
        const string sql = """
            SELECT ProfileId, Context, ApplyCount, SuccessCount, LastAppliedUtc
            FROM ProfileContextAssociations
            WHERE ProfileId = @profileId AND Context = @context
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["profileId"] = profileId,
            ["context"] = context
        });

        return rows.Count == 0 ? null : MapAssociation(rows[0]);
    }

    public async Task<List<ProfileContextAssociation>> GetBestProfilesForContextAsync(string context, int count = 5)
    {
        const string sql = """
            SELECT ProfileId, Context, ApplyCount, SuccessCount, LastAppliedUtc
            FROM ProfileContextAssociations
            WHERE Context = @context AND ApplyCount > 0
            ORDER BY (CAST(SuccessCount AS REAL) / ApplyCount) DESC, ApplyCount DESC
            LIMIT @count
            """;

        var rows = await db.ExecuteQueryAsync(sql, new Dictionary<string, object>
        {
            ["context"] = context,
            ["count"] = count
        });

        return rows.Select(MapAssociation).ToList();
    }

    private async Task UpsertAssociationAsync(string profileId, string context, int applyDelta, int successDelta)
    {
        const string sql = """
            INSERT INTO ProfileContextAssociations
                (ProfileId, Context, ApplyCount, SuccessCount, LastAppliedUtc, UpdatedAt)
            VALUES (@profileId, @context, @applyDelta, @successDelta, @now, @now)
            ON CONFLICT(ProfileId, Context) DO UPDATE SET
                ApplyCount = ApplyCount + @applyDelta,
                SuccessCount = SuccessCount + @successDelta,
                LastAppliedUtc = CASE WHEN @applyDelta > 0 THEN @now ELSE LastAppliedUtc END,
                UpdatedAt = @now
            """;

        await db.ExecuteNonQueryAsync(sql, new Dictionary<string, object>
        {
            ["profileId"] = profileId,
            ["context"] = context,
            ["applyDelta"] = applyDelta,
            ["successDelta"] = successDelta,
            ["now"] = DateTime.UtcNow.ToString("O")
        });
    }

    private static ProfileContextAssociation MapAssociation(Dictionary<string, object> row) => new()
    {
        ProfileId = row["ProfileId"].ToString()!,
        Context = row["Context"].ToString()!,
        ApplyCount = Convert.ToInt32(row["ApplyCount"]),
        SuccessCount = Convert.ToInt32(row["SuccessCount"]),
        LastAppliedUtc = row["LastAppliedUtc"] == null ? null : DateTime.Parse(row["LastAppliedUtc"].ToString()!)
    };
}
