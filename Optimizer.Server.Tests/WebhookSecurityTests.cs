using System.Net;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

/// <summary>
/// Negative security tests for webhook SSRF protection (Fix 2).
///
/// Tests <see cref="WebhookService.IsDisallowedAddress"/> directly with IP literals so
/// no DNS resolution is needed — tests are hermetic and fast.
///
/// A separate acceptance test uses a well-known public IP literal (93.184.216.34,
/// one of example.com's addresses) to verify that public IPs are not mistakenly blocked.
/// </summary>
public class WebhookSecurityTests
{
    // ── IsDisallowedAddress unit tests (hermetic — no DNS) ────────────────────

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.2")]
    [InlineData("::1")]
    public void IsDisallowedAddress_Loopback_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"Loopback address {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("169.254.0.1")]
    [InlineData("169.254.169.254")]   // AWS metadata endpoint
    [InlineData("169.254.100.100")]
    public void IsDisallowedAddress_LinkLocal_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"Link-local address {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    public void IsDisallowedAddress_Rfc1918_10Block_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"RFC-1918 10.0.0.0/8 address {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("172.16.0.1")]
    [InlineData("172.20.0.1")]
    [InlineData("172.31.255.255")]
    public void IsDisallowedAddress_Rfc1918_172Block_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"RFC-1918 172.16.0.0/12 address {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.255.255")]
    public void IsDisallowedAddress_Rfc1918_192Block_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"RFC-1918 192.168.0.0/16 address {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("fe80::1")]         // IPv6 link-local
    [InlineData("fe80::1%eth0")]    // link-local with zone ID
    public void IsDisallowedAddress_IPv6LinkLocal_ReturnsTrue(string ip)
    {
        // Strip zone ID for parsing
        var stripped = ip.Split('%')[0];
        var addr = IPAddress.Parse(stripped);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"IPv6 link-local {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]  // unique-local
    public void IsDisallowedAddress_IPv6UniqueLocal_ReturnsTrue(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.True(WebhookService.IsDisallowedAddress(addr),
            $"IPv6 unique-local {ip} must be disallowed.");
    }

    [Theory]
    [InlineData("93.184.216.34")]    // example.com
    [InlineData("1.1.1.1")]          // Cloudflare DNS
    [InlineData("8.8.8.8")]          // Google DNS
    [InlineData("2606:2800:21f:cb07:6820:80da:af6b:8b2c")]  // example.com IPv6
    public void IsDisallowedAddress_PublicAddress_ReturnsFalse(string ip)
    {
        var addr = IPAddress.Parse(ip);
        Assert.False(WebhookService.IsDisallowedAddress(addr),
            $"Public address {ip} must NOT be disallowed.");
    }

    // ── ValidateUrl integration tests (IP literal URLs — no DNS needed) ───────

    [Theory]
    [InlineData("http://localhost/x")]
    [InlineData("http://localhost:8080/evil")]
    public void ValidateUrl_Localhost_Throws(string url)
    {
        // 'localhost' resolves to loopback — blocked
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl(url));
    }

    [Fact]
    public void ValidateUrl_LoopbackIpLiteral_Throws()
    {
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("http://127.0.0.1/x"));
    }

    [Fact]
    public void ValidateUrl_LinkLocalMetadata_Throws()
    {
        // AWS instance metadata endpoint — the classic SSRF target
        Assert.Throws<ArgumentException>(() =>
            WebhookService.ValidateUrl("http://169.254.169.254/latest/meta-data/"));
    }

    [Fact]
    public void ValidateUrl_PrivateSubnet_192_Throws()
    {
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("http://192.168.1.1/hook"));
    }

    [Fact]
    public void ValidateUrl_PrivateSubnet_10_Throws()
    {
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("http://10.0.0.1/hook"));
    }

    [Fact]
    public void ValidateUrl_PrivateSubnet_172_Throws()
    {
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("http://172.16.0.1/hook"));
    }

    [Fact]
    public void ValidateUrl_PublicIpLiteral_DoesNotThrow()
    {
        // Uses a known public IP literal to avoid DNS — tests the happy path without
        // depending on network connectivity.
        // 93.184.216.34 is a well-known IP for example.com.
        var ex = Record.Exception(() =>
            WebhookService.ValidateUrl("https://93.184.216.34/hook"));
        Assert.Null(ex);
    }

    [Fact]
    public void ValidateUrl_FtpScheme_Throws()
    {
        // Non-http/https schemes must still be rejected
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("ftp://93.184.216.34/hook"));
    }

    [Fact]
    public void ValidateUrl_RelativeUrl_Throws()
    {
        Assert.Throws<ArgumentException>(() => WebhookService.ValidateUrl("/relative/path"));
    }
}
