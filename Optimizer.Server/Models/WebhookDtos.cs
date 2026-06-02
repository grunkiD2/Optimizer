namespace Optimizer.Server.Models;

public record CreateWebhookRequest(string Url, IReadOnlyList<string>? EventTypes);

public record WebhookDto(
    Guid Id,
    string Url,
    IReadOnlyList<string> EventTypes,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastDeliveryAtUtc,
    int ConsecutiveFailures);

/// <summary>Secret is returned only on creation and never again.</summary>
public record CreatedWebhookDto(
    Guid Id,
    string Url,
    string Secret,
    IReadOnlyList<string> EventTypes);

public record IncomingEventDto(
    string Type,
    string Title,
    string Detail,
    DateTime TimestampUtc,
    Dictionary<string, string>? Data);
