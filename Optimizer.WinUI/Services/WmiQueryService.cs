using System.Collections.Concurrent;
using System.Management;

namespace Optimizer.WinUI.Services;

public class WmiQueryService : IWmiQueryService
{
    private record CacheEntry(DateTime ExpiresUtc, object Data);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    public async Task<IReadOnlyList<T>> QueryAsync<T>(
        string wql,
        Func<ManagementObject, T> map,
        TimeSpan? cacheTtl = null,
        string? scope = null)
    {
        var cacheKey = $"{scope ?? ""}|{wql}";

        if (cacheTtl.HasValue && _cache.TryGetValue(cacheKey, out var entry))
        {
            if (entry.ExpiresUtc > DateTime.UtcNow && entry.Data is IReadOnlyList<T> cached)
                return cached;
        }

        var result = await Task.Run(() =>
        {
            var list = new List<T>();
            try
            {
                using var searcher = scope == null
                    ? new ManagementObjectSearcher(wql)
                    : new ManagementObjectSearcher(scope, wql);
                foreach (ManagementObject obj in searcher.Get())
                {
                    try { list.Add(map(obj)); }
                    catch (Exception ex) { EngineLog.Error($"WMI map failed for {wql}", ex); }
                    finally { obj.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                EngineLog.Error($"WMI query failed: {wql}", ex);
            }
            return list;
        });

        if (cacheTtl.HasValue && result.Count > 0)
        {
            _cache[cacheKey] = new CacheEntry(DateTime.UtcNow + cacheTtl.Value, result);
        }

        return result;
    }

    public void InvalidateCache() => _cache.Clear();

    public void InvalidateCache(string wql)
    {
        foreach (var key in _cache.Keys
            .Where(k => k.Contains(wql, StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            _cache.TryRemove(key, out _);
        }
    }
}
