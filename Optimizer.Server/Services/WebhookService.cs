using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Optimizer.Server.Data;
using Optimizer.Server.Data.Entities;
using Optimizer.Server.Models;

namespace Optimizer.Server.Services;

public class WebhookService : IWebhookService
{
    private const int MaxConsecutiveFailures = 10;
    private static readonly int[] RetryDelaysMs = [1_000, 3_000]; // 2 retries after initial attempt
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    private readonly OptimizerDbContext _db;
    private readonly HttpClient _http;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(OptimizerDbContext db, IHttpClientFactory httpClientFactory, ILogger<WebhookService> logger)
    {
        _db     = db;
        _http   = httpClientFactory.CreateClient("webhook");
        _logger = logger;
    }

    /// <summary>Test-friendly constructor that accepts a pre-configured HttpClient directly.</summary>
    internal WebhookService(OptimizerDbContext db, HttpClient http, ILogger<WebhookService> logger)
    {
        _db     = db;
        _http   = http;
        _logger = logger;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    public async Task<CreatedWebhookDto> CreateAsync(Guid userId, CreateWebhookRequest req)
    {
        ValidateUrl(req.Url);

        // Generate a random signing secret (32 random bytes → base64url, ~43 chars)
        var secretBytes = RandomNumberGenerator.GetBytes(32);
        var secret = Convert.ToBase64String(secretBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var eventTypes = req.EventTypes ?? [];
        var csv = string.Join(",", eventTypes.Select(t => t.Trim()).Where(t => t.Length > 0));

        var sub = new WebhookSubscription
        {
            UserId        = userId,
            Url           = req.Url.TrimEnd('/'),
            Secret        = secret,
            EventTypesCsv = csv
        };

        _db.WebhookSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        return new CreatedWebhookDto(sub.Id, sub.Url, secret, ParseCsv(csv));
    }

    public async Task<IReadOnlyList<WebhookDto>> ListAsync(Guid userId)
    {
        var subs = await _db.WebhookSubscriptions
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync();

        return subs.Select(MapToDto).ToList();
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id)
    {
        var sub = await _db.WebhookSubscriptions
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (sub == null) return false;

        _db.WebhookSubscriptions.Remove(sub);
        await _db.SaveChangesAsync();
        return true;
    }

    // ── Dispatch ──────────────────────────────────────────────────────────────

    public async Task DispatchAsync(Guid userId, IncomingEventDto evt)
    {
        var subs = await _db.WebhookSubscriptions
            .Where(w => w.UserId == userId && w.IsActive)
            .ToListAsync();

        var matching = subs.Where(s => MatchesEventType(s, evt.Type)).ToList();
        if (matching.Count == 0) return;

        // Serialize the payload once for all subscriptions
        var payload = JsonSerializer.Serialize(new
        {
            type         = evt.Type,
            title        = evt.Title,
            detail       = evt.Detail,
            timestampUtc = evt.TimestampUtc,
            data         = evt.Data
        });

        foreach (var sub in matching)
        {
            await DeliverWithRetryAsync(sub, evt.Type, payload);
        }
    }

    // ── Delivery ──────────────────────────────────────────────────────────────

    private async Task DeliverWithRetryAsync(WebhookSubscription sub, string eventType, string payload)
    {
        int attempt = 0;
        bool success = false;
        int lastStatus = 0;
        string? lastError = null;

        // Total of 3 attempts: initial + 2 retries
        var delaySchedule = new int[] { 0 }.Concat(RetryDelaysMs).ToArray();

        foreach (var delayMs in delaySchedule)
        {
            if (delayMs > 0)
                await Task.Delay(delayMs);

            attempt++;
            (success, lastStatus, lastError) = await TrySendAsync(sub, payload);

            if (success) break;
        }

        // Record delivery
        var delivery = new WebhookDelivery
        {
            SubscriptionId = sub.Id,
            EventType      = eventType,
            StatusCode     = lastStatus,
            Success        = success,
            Error          = success ? null : lastError,
            AttemptNumber  = attempt
        };
        _db.WebhookDeliveries.Add(delivery);

        if (success)
        {
            sub.LastDeliveryAtUtc     = DateTime.UtcNow;
            sub.ConsecutiveFailures   = 0;
        }
        else
        {
            sub.ConsecutiveFailures++;
            if (sub.ConsecutiveFailures >= MaxConsecutiveFailures)
            {
                sub.IsActive = false;
                _logger.LogWarning(
                    "Webhook {Id} auto-disabled after {N} consecutive failures.",
                    sub.Id, sub.ConsecutiveFailures);
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task<(bool Success, int StatusCode, string? Error)> TrySendAsync(
        WebhookSubscription sub, string payload)
    {
        try
        {
            var bodyBytes = Encoding.UTF8.GetBytes(payload);
            var signature = ComputeSignature(bodyBytes, sub.Secret);

            using var cts = new CancellationTokenSource(DeliveryTimeout);
            using var req = new HttpRequestMessage(HttpMethod.Post, sub.Url);
            req.Content = new ByteArrayContent(bodyBytes);
            req.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            req.Headers.Add("X-Optimizer-Signature", $"sha256={signature}");
            req.Headers.Add("X-Optimizer-Event", sub.EventTypesCsv.Length == 0 ? "all" : sub.EventTypesCsv);

            using var resp = await _http.SendAsync(req, cts.Token);

            if (resp.IsSuccessStatusCode)
                return (true, (int)resp.StatusCode, null);

            return (false, (int)resp.StatusCode,
                $"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ComputeSignature(byte[] bodyBytes, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool MatchesEventType(WebhookSubscription sub, string eventType)
    {
        if (string.IsNullOrEmpty(sub.EventTypesCsv)) return true; // subscribe to all
        var types = sub.EventTypesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return types.Any(t => string.Equals(t.Trim(), eventType, StringComparison.OrdinalIgnoreCase));
    }

    // ── SSRF protection ───────────────────────────────────────────────────────

    /// <summary>
    /// Validates a webhook URL against SSRF attacks.
    ///
    /// Checks performed:
    ///   1. Must be an absolute http or https URL.
    ///   2. The target host is resolved to IP address(es).  If the host is already an IP literal
    ///      it is checked directly; otherwise all addresses returned by DNS are checked.
    ///   3. Any loopback, link-local (169.254.x.x / fe80::), or RFC-1918/unique-local private
    ///      address is rejected.
    ///
    /// Note: DNS-rebinding attacks (where DNS returns a public IP at registration time but a
    /// private IP at delivery time) are NOT prevented here.  Full protection would require
    /// re-resolving and re-checking at every delivery attempt.
    /// </summary>
    internal static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("Webhook URL must be a valid absolute http or https URL.", nameof(url));
        }

        var host = uri.Host;

        // Collect all IP addresses for this host
        IEnumerable<IPAddress> addresses;

        if (IPAddress.TryParse(host, out var literal))
        {
            // URL contained an IP literal (e.g. http://127.0.0.1/...)
            addresses = [literal];
        }
        else
        {
            // Hostname — resolve via DNS (blocks if the resolver is slow; acceptable at registration time)
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Webhook URL host '{host}' could not be resolved: {ex.Message}", nameof(url));
            }
        }

        foreach (var addr in addresses)
        {
            if (IsDisallowedAddress(addr))
            {
                throw new ArgumentException(
                    $"Webhook URL resolves to a disallowed address ({addr}). " +
                    "Loopback, link-local, and private-subnet targets are not permitted.",
                    nameof(url));
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the address is loopback, link-local (169.254.0.0/16 or fe80::/10),
    /// or any of the RFC-1918 / unique-local private ranges
    /// (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, fc00::/7).
    ///
    /// Extracted as a separate method so it can be unit-tested directly with IP literals,
    /// avoiding DNS dependency in tests.
    /// </summary>
    internal static bool IsDisallowedAddress(IPAddress addr)
    {
        if (addr.IsIPv4MappedToIPv6)
            addr = addr.MapToIPv4();

        if (IPAddress.IsLoopback(addr))           return true;
        if (addr.IsIPv6LinkLocal)                 return true;  // fe80::/10
        if (addr.IsIPv6UniqueLocal)               return true;  // fc00::/7

        var bytes = addr.GetAddressBytes();

        if (bytes.Length == 4)
        {
            // 169.254.0.0/16 (link-local)
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12  (172.16.x.x – 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
        }

        if (bytes.Length == 16)
        {
            // fc00::/7 unique-local (fc00:: and fd00::)
            if ((bytes[0] & 0xFE) == 0xFC) return true;
            // fe80::/10 link-local
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
        }

        return false;
    }

    private static WebhookDto MapToDto(WebhookSubscription s) => new(
        s.Id,
        s.Url,
        ParseCsv(s.EventTypesCsv),
        s.IsActive,
        s.CreatedAtUtc,
        s.LastDeliveryAtUtc,
        s.ConsecutiveFailures);

    private static IReadOnlyList<string> ParseCsv(string csv)
        => csv.Split(',', StringSplitOptions.RemoveEmptyEntries)
              .Select(t => t.Trim())
              .Where(t => t.Length > 0)
              .ToList()
              .AsReadOnly();
}
