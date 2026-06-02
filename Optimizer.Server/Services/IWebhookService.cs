using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public interface IWebhookService
{
    Task<CreatedWebhookDto> CreateAsync(Guid userId, CreateWebhookRequest req);
    Task<IReadOnlyList<WebhookDto>> ListAsync(Guid userId);
    Task<bool> DeleteAsync(Guid userId, Guid id);

    /// <summary>
    /// Called when an event arrives for a user. Fans out to all matching active subscriptions.
    /// Dispatch is fire-and-forget on a background task — callers receive no delivery result.
    /// </summary>
    Task DispatchAsync(Guid userId, IncomingEventDto evt);
}
