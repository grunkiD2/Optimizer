using System;
using System.IO;
using Optimizer.WinUI.Services.Cloud;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for SyncTombstoneCollector — record, get-and-clear, de-dupe, and persistence.
/// Uses a temp file path for each test so tests are fully isolated.
/// </summary>
public class SyncTombstoneCollectorTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(
        Path.GetTempPath(), $"tombstone-test-{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
        GC.SuppressFinalize(this);
    }

    private SyncTombstoneCollector Create() => new(_tempFile);

    [Fact]
    public void NewCollector_StartsEmpty()
    {
        var c = Create();
        var items = c.GetAndClear();
        Assert.Empty(items);
    }

    [Fact]
    public void Record_AddsEntry()
    {
        var c = Create();
        c.Record("snapshot", "snap-1");

        var items = c.GetAndClear();
        Assert.Single(items);
        Assert.Equal("snapshot", items[0].ItemType);
        Assert.Equal("snap-1", items[0].ItemId);
    }

    [Fact]
    public void Record_Deduplicates_SameTypeAndId()
    {
        var c = Create();
        c.Record("snapshot", "snap-1");
        c.Record("snapshot", "snap-1");  // duplicate

        var items = c.GetAndClear();
        Assert.Single(items);
    }

    [Fact]
    public void Record_AllowsDifferentTypes_SameId()
    {
        var c = Create();
        c.Record("snapshot", "id-1");
        c.Record("history", "id-1");

        var items = c.GetAndClear();
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void GetAndClear_ClearsAfterReturn()
    {
        var c = Create();
        c.Record("snapshot", "snap-1");
        c.GetAndClear();

        var second = c.GetAndClear();
        Assert.Empty(second);
    }

    [Fact]
    public void GetAndClear_ReturnsAllRecordedItems()
    {
        var c = Create();
        c.Record("snapshot", "s1");
        c.Record("history", "h1");
        c.Record("history", "h2");

        var items = c.GetAndClear();
        Assert.Equal(3, items.Count);
        Assert.Contains(items, i => i.ItemType == "snapshot" && i.ItemId == "s1");
        Assert.Contains(items, i => i.ItemType == "history" && i.ItemId == "h1");
        Assert.Contains(items, i => i.ItemType == "history" && i.ItemId == "h2");
    }

    [Fact]
    public void Persistence_SurvivesRestart()
    {
        // Record then abandon without clearing
        var c1 = Create();
        c1.Record("snapshot", "persistent-1");
        c1.Record("history", "persistent-2");

        // New instance loading same file should see the records
        var c2 = new SyncTombstoneCollector(_tempFile);
        var items = c2.GetAndClear();

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.ItemId == "persistent-1");
        Assert.Contains(items, i => i.ItemId == "persistent-2");
    }

    [Fact]
    public void Persistence_AfterGetAndClear_FileIsEmpty()
    {
        var c1 = Create();
        c1.Record("snapshot", "s1");
        c1.GetAndClear();

        // File should now persist an empty list
        var c2 = new SyncTombstoneCollector(_tempFile);
        var items = c2.GetAndClear();
        Assert.Empty(items);
    }

    [Fact]
    public void DeletedAtUtc_IsRecentTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var c = Create();
        c.Record("snapshot", "s1");
        var after = DateTime.UtcNow.AddSeconds(1);

        var items = c.GetAndClear();
        Assert.True(items[0].DeletedAtUtc >= before && items[0].DeletedAtUtc <= after);
    }
}
