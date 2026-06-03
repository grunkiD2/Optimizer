using System.Globalization;

namespace Optimizer.WinUI.Services.Data;

/// <summary>
/// A single query result row with typed accessors, replacing scattered
/// <c>Convert.ToInt32(row["x"])</c> / <c>DateTime.Parse(row["x"].ToString()!)</c> boilerplate.
/// The <see cref="this[string]"/> indexer is kept for backward compatibility with older callers.
/// </summary>
public sealed class DbRow(IReadOnlyDictionary<string, object?> values)
{
    /// <summary>Raw value (or null). Back-compat shim; prefer the typed getters below.</summary>
    public object? this[string column] => values.TryGetValue(column, out var v) ? v : null;

    public bool IsNull(string column) => this[column] is null;

    public string GetString(string column) => this[column]?.ToString() ?? string.Empty;

    public string? GetStringOrNull(string column)
    {
        var s = this[column]?.ToString();
        return string.IsNullOrEmpty(s) ? null : s;
    }

    public int GetInt(string column) => this[column] is { } v ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;

    public long GetLong(string column) => this[column] is { } v ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : 0;

    public double GetDouble(string column) => this[column] is { } v ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;

    public bool GetBool(string column) => GetInt(column) != 0;

    public DateTime GetDateTime(string column) =>
        DateTime.Parse(GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public DateTime? GetDateTimeOrNull(string column)
    {
        var s = GetStringOrNull(column);
        return s is null ? null : DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
