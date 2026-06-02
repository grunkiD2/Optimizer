namespace Optimizer.Server.Data.Entities;

public class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Url { get; set; } = "";
    public string Secret { get; set; } = "";           // HMAC signing secret (generated, stored plain)
    public string EventTypesCsv { get; set; } = "";    // "" = all; else comma list
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastDeliveryAtUtc { get; set; }
    public int ConsecutiveFailures { get; set; }
}

public class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public string EventType { get; set; } = "";
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
    public int AttemptNumber { get; set; }
}
