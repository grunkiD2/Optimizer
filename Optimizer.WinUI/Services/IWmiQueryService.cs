namespace Optimizer.WinUI.Services;

public interface IWmiQueryService
{
    /// <summary>
    /// Run a WMI query, optionally cached by query string for a time window.
    /// Pass cacheTtl = null to bypass cache.
    /// </summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(
        string wql,
        Func<System.Management.ManagementObject, T> map,
        TimeSpan? cacheTtl = null,
        string? scope = null);

    void InvalidateCache();
    void InvalidateCache(string wql);
}
