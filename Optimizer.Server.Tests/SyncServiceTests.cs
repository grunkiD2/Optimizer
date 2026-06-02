using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class SyncServiceTests : IDisposable
{
    private readonly OptimizerDbContext _db;
    private readonly SyncService _sync;
    private readonly Guid _userId = Guid.NewGuid();

    public SyncServiceTests()
    {
        var opt = new DbContextOptionsBuilder<OptimizerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new OptimizerDbContext(opt);
        _db.Users.Add(new User { Id = _userId, Email = "test@example.com" });
        _db.SaveChanges();
        _sync = new SyncService(_db);
    }

    public void Dispose() { _db.Dispose(); GC.SuppressFinalize(this); }

    [Fact]
    public async Task PullFromZero_ReturnsEmpty_NewUser()
    {
        var r = await _sync.PullAsync(_userId, 0);
        Assert.Empty(r.Items);
        Assert.Equal(0, r.Cursor);
    }

    [Fact]
    public async Task Push_AssignsIncrementingVersions()
    {
        var req = new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{}"),
            new SyncPushItem("profile", "p2", "{}"),
        });
        var r = await _sync.PushAsync(_userId, req);
        Assert.Equal(2, r.Results.Count);
        Assert.Equal(1, r.Results[0].Version);
        Assert.Equal(2, r.Results[1].Version);
        Assert.Equal(2, r.ServerVersion);
    }

    [Fact]
    public async Task PullAfterPush_ReturnsItems()
    {
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{\"name\":\"A\"}")
        }));
        var r = await _sync.PullAsync(_userId, 0);
        Assert.Single(r.Items);
        Assert.Equal("p1", r.Items[0].ItemId);
        Assert.Equal(1, r.Cursor);
    }

    [Fact]
    public async Task PushUpdate_BumpsVersion_AndReplacesPayload()
    {
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{\"v\":1}")
        }));
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{\"v\":2}")
        }));
        var r = await _sync.PullAsync(_userId, 0);
        Assert.Single(r.Items);
        Assert.Equal(2, r.Items[0].Version);
        Assert.Contains("\"v\":2", r.Items[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PullWithCursor_OnlyReturnsNewer()
    {
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{}"),
            new SyncPushItem("profile", "p2", "{}")
        }));
        var first = await _sync.PullAsync(_userId, 0);
        var second = await _sync.PullAsync(_userId, first.Cursor);
        Assert.Empty(second.Items);
    }

    [Fact]
    public async Task PushTombstone_IsReturnedOnPull()
    {
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{}", IsDeleted: true)
        }));
        var r = await _sync.PullAsync(_userId, 0);
        Assert.Single(r.Items);
        Assert.True(r.Items[0].IsDeleted);
    }

    [Fact]
    public async Task PushUnknownType_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sync.PushAsync(_userId, new SyncPushRequest(new[]
            {
                new SyncPushItem("badtype", "p1", "{}")
            })));
    }

    [Fact]
    public async Task UserIsolation_Works()
    {
        var otherUser = Guid.NewGuid();
        _db.Users.Add(new User { Id = otherUser, Email = "other@example.com" });
        _db.SaveChanges();

        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("profile", "p1", "{}")
        }));

        var r = await _sync.PullAsync(otherUser, 0);
        Assert.Empty(r.Items);
    }

    [Fact]
    public async Task ServerVersion_ReflectsAllPushes()
    {
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("snapshot", "s1", "{}")
        }));
        var r = await _sync.PullAsync(_userId, 0);
        Assert.Equal(1, r.ServerVersion);
        Assert.Equal(1, r.Cursor);
    }

    [Fact]
    public async Task Push_EmptyItems_ReturnsEmptyResults()
    {
        var r = await _sync.PushAsync(_userId, new SyncPushRequest(Array.Empty<SyncPushItem>()));
        Assert.Empty(r.Results);
        Assert.Equal(0, r.ServerVersion);
    }

    [Fact]
    public async Task PushTombstone_ThenPullDelta_ShowsDelete()
    {
        // Push initial snapshot
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("snapshot", "s1", "{}")
        }));
        var first = await _sync.PullAsync(_userId, 0);
        Assert.Single(first.Items);
        Assert.False(first.Items[0].IsDeleted);

        // Push tombstone for the same item
        await _sync.PushAsync(_userId, new SyncPushRequest(new[]
        {
            new SyncPushItem("snapshot", "s1", "{}", IsDeleted: true)
        }));

        // Delta pull from the cursor after the first pull should include only the tombstone
        var second = await _sync.PullAsync(_userId, first.Cursor);
        Assert.Single(second.Items);
        Assert.True(second.Items[0].IsDeleted);
        Assert.Equal("s1", second.Items[0].ItemId);
    }

    [Fact]
    public async Task AllowedTypes_AllAccepted()
    {
        var items = new[]
        {
            new SyncPushItem("profile",  "id1", "{}"),
            new SyncPushItem("snapshot", "id2", "{}"),
            new SyncPushItem("history",  "id3", "{}"),
            new SyncPushItem("settings", "id4", "{}"),
        };
        var r = await _sync.PushAsync(_userId, new SyncPushRequest(items));
        Assert.Equal(4, r.Results.Count);
    }
}
