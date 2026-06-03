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

    /// <summary>Test-only: point the service at a specific database file (e.g. a temp path).</summary>
    internal DatabaseService(string dbFilePath)
    {
        _connectionString = $"Data Source={dbFilePath};Cache=Shared";
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

    /// <summary>Execute a query returning multiple rows.</summary>
    public async Task<List<Dictionary<string, object>>> ExecuteQueryAsync(
        string sql,
        Dictionary<string, object>? parameters = null)
    {
        var results = new List<Dictionary<string, object>>();
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
            var row = new Dictionary<string, object>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.GetValue(i);
                row[reader.GetName(i)] = value == DBNull.Value ? null! : value;
            }
            results.Add(row);
        }

        return results;
    }

    /// <summary>Backup database to JSON (for export).</summary>
    public async Task BackupToJsonAsync(string outputPath)
    {
        // Placeholder for Phase 8
        await Task.CompletedTask;
    }

    /// <summary>Restore database from JSON.</summary>
    public async Task RestoreFromJsonAsync(string inputPath)
    {
        // Placeholder for Phase 8
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
}
