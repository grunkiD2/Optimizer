using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public interface ISyncService
{
    Task<SyncPullResponse> PullAsync(Guid userId, long cursor);
    Task<SyncPushResponse> PushAsync(Guid userId, SyncPushRequest request);
}
