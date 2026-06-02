using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public class SyncService : ISyncService
{
    private readonly OptimizerDbContext _db;
    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "profile", "snapshot", "history", "settings"
    };

    public SyncService(OptimizerDbContext db) => _db = db;

    public async Task<SyncPullResponse> PullAsync(Guid userId, long cursor)
    {
        var serverVersion = await GetCurrentVersionAsync(userId);

        const int MaxItemsPerPull = 500;
        var items = await _db.SyncItems
            .Where(s => s.UserId == userId && s.Version > cursor)
            .OrderBy(s => s.Version)
            .Take(MaxItemsPerPull)
            .Select(s => new SyncItemDto(
                s.ItemType, s.ItemId, s.Version, s.UpdatedAtUtc, s.Payload, s.IsDeleted))
            .ToListAsync();

        var newCursor = items.Count > 0 ? items[^1].Version : cursor;
        return new SyncPullResponse(newCursor, serverVersion, items);
    }

    public async Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request)
    {
        var results = new List<SyncPushResult>();

        // Validate first
        foreach (var item in request.Items)
        {
            if (!AllowedTypes.Contains(item.ItemType))
                throw new InvalidOperationException($"Unknown item type: {item.ItemType}");
            if (string.IsNullOrEmpty(item.ItemId) || item.ItemId.Length > 128)
                throw new InvalidOperationException($"Invalid item id: {item.ItemId}");
        }

        // Atomically increment version and persist all items.
        // We rely on SaveChangesAsync's implicit transaction (SQLite wraps the whole
        // set of changes; in-memory provider is inherently atomic).
        var counter = await _db.UserVersionCounters.FindAsync(userId);
        if (counter == null)
        {
            counter = new UserVersionCounter { UserId = userId, CurrentVersion = 0 };
            _db.UserVersionCounters.Add(counter);
        }

        foreach (var item in request.Items)
        {
            counter.CurrentVersion++;
            var newVersion = counter.CurrentVersion;

            var existing = await _db.SyncItems
                .FirstOrDefaultAsync(s => s.UserId == userId && s.ItemType == item.ItemType && s.ItemId == item.ItemId);

            if (existing != null)
            {
                existing.Version = newVersion;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                existing.Payload = item.Payload;
                existing.IsDeleted = item.IsDeleted;
            }
            else
            {
                _db.SyncItems.Add(new SyncItem
                {
                    UserId = userId,
                    ItemType = item.ItemType,
                    ItemId = item.ItemId,
                    Version = newVersion,
                    Payload = item.Payload,
                    IsDeleted = item.IsDeleted
                });
            }

            results.Add(new SyncPushResult(item.ItemType, item.ItemId, newVersion));
        }

        await _db.SaveChangesAsync();

        return new SyncPushResponse(counter.CurrentVersion, results);
    }

    private async Task<long> GetCurrentVersionAsync(Guid userId)
    {
        var counter = await _db.UserVersionCounters.FindAsync(userId);
        return counter?.CurrentVersion ?? 0;
    }
}
