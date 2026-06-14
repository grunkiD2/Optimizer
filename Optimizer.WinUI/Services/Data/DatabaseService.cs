using Microsoft.Data.Sqlite;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Data;

/// <summary>Manages SQLite database connection, migrations, and lifecycle.</summary>
public class DatabaseService : IAsyncDisposable
{
    private readonly string _connectionString;
    private bool _initialized;

    public DatabaseService()
    {
        AppPaths.EnsureFolderExists();
        var dbPath = AppPaths.GetDataFile("optimizer.db");
        _connectionString = $"Data Source={dbPath};Cache=Shared";
    }

    /// <summary>
    /// Test-only: point the service at a specific database file (e.g. a temp path). Pooling is
    /// disabled so each connection closes deterministically — parallel test classes can't clash on
    /// a shared connection pool, and the temp file is releasable for deletion right after a test.
    /// </summary>
    internal DatabaseService(string dbFilePath)
    {
        _connectionString = $"Data Source={dbFilePath};Cache=Shared;Pooling=False";
    }

    /// <summary>Initialize database: create tables, run migrations.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Enable foreign keys
        await using var fkCmd = conn.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync();

        // Create tables
        foreach (var sql in DatabaseSchema.Tables)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // Run idempotent column migrations for databases from an older schema.
        // "duplicate column name" means the column already exists — safe to ignore.
        foreach (var sql in DatabaseSchema.Migrations)
        {
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Microsoft.Data.Sqlite.SqliteException ex)
                when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // Column already present — expected on fresh installs.
            }
        }

        // Insert initial data
        foreach (var sql in DatabaseSchema.InitialData)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }

        // R4 one-time drop-and-relearn: every Power/Metric baseline learned before the context
        // AUTHORITY existed was keyed by the process-list GUESS (live audit: "Gaming" while the
        // brain measured IDLE) — mislabeled rows poison drift detection for weeks. The 72 h
        // half-life rebuilds clean baselines in days. Marker row makes this idempotent.
        await using (var marker = conn.CreateCommand())
        {
            marker.CommandText = "SELECT COUNT(*) FROM Metadata WHERE Key = 'ContextAuthorityBaselineReset'";
            if (Convert.ToInt32(await marker.ExecuteScalarAsync()) == 0)
            {
                await using var wipe = conn.CreateCommand();
                wipe.CommandText = """
                    DELETE FROM PowerBaselines;
                    DELETE FROM MetricBaselines;
                    INSERT INTO Metadata (Key, Value) VALUES ('ContextAuthorityBaselineReset', datetime('now'));
                    """;
                await wipe.ExecuteNonQueryAsync();
                EngineLog.Write("R4: dropped Power/Metric baselines for relearn under the context authority");
            }
        }

        _initialized = true;
        EngineLog.Write("Database initialized successfully");
    }

    /// <summary>Get an open database connection (caller must dispose).</summary>
    public SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();

        // Enable foreign keys per-connection
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON";
        cmd.ExecuteNonQuery();

        return conn;
    }

    /// <summary>
    /// Create a <em>closed</em> connection over the same DB file/connection string. The caller opens it
    /// (e.g. <c>await conn.OpenAsync()</c>). This is the connection factory for services that manage their
    /// own open/close lifecycle — notably <c>ProfileTransitionWatcher</c> / <c>ProfileOutcomesService</c>
    /// (Profil 2.0 — Fase 2), wired in DI as <c>() =&gt; db.CreateConnection()</c> so timeline/outcomes land
    /// in the SAME database as the rest of the Optimizer.
    /// <para>Unlike <see cref="GetConnection"/>, this deliberately does NOT apply the per-connection pragmas
    /// (<c>PRAGMA foreign_keys = ON</c> etc.) — it just hands back a closed connection. A caller that needs FK
    /// enforcement must set the pragma itself after opening. The ProfileTimeline / ProfileOutcomes tables
    /// declare no foreign keys, so for the current callers this is moot.</para>
    /// </summary>
    public SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>Execute a query returning a single scalar value.</summary>
    public async Task<T?> ExecuteScalarAsync<T>(string sql, Dictionary<string, object>? parameters = null)
    {
        await using var conn = GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
                cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }

        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? default : (T)Convert.ChangeType(result, typeof(T));
    }

    /// <summary>Execute a non-query command (INSERT, UPDATE, DELETE).</summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        await using var conn = GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
                cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }

        return await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Execute a query returning multiple rows (use the typed getters on <see cref="DbRow"/>).</summary>
    public async Task<List<DbRow>> ExecuteQueryAsync(
        string sql,
        Dictionary<string, object>? parameters = null)
    {
        var results = new List<DbRow>();
        await using var conn = GetConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
                cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[reader.GetName(i)] = value == DBNull.Value ? null : value;
            }
            results.Add(new DbRow(row));
        }

        return results;
    }

    /// <summary>
    /// Run several statements atomically in a single transaction. Use for batch rebuilds
    /// (e.g. DELETE-all then re-INSERT) so a mid-operation failure can't leave a partial table.
    /// </summary>
    public async Task RunInTransactionAsync(Func<DbBatch, Task> action)
    {
        await using var conn = GetConnection();
        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync();
        try
        {
            await action(new DbBatch(conn, tx));
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>Create a consistent backup copy of the database to a .db file (online backup API).</summary>
    public Task BackupToFileAsync(string destPath) => Task.Run(() =>
    {
        using var source = GetConnection();
        using var dest = new SqliteConnection($"Data Source={destPath}");
        dest.Open();
        source.BackupDatabase(dest);
    });

    /// <summary>Restore the database in place from a backup .db file produced by <see cref="BackupToFileAsync"/>.</summary>
    public Task RestoreFromFileAsync(string sourcePath) => Task.Run(() =>
    {
        using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
        source.Open();
        using var dest = GetConnection();
        source.BackupDatabase(dest);
    });

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}

/// <summary>A set of statements executed against one shared connection + transaction.</summary>
public sealed class DbBatch(SqliteConnection connection, SqliteTransaction transaction)
{
    public async Task<int> ExecuteNonQueryAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        if (parameters != null)
        {
            foreach (var (key, value) in parameters)
                cmd.Parameters.AddWithValue($"@{key}", value ?? DBNull.Value);
        }
        return await cmd.ExecuteNonQueryAsync();
    }
}
