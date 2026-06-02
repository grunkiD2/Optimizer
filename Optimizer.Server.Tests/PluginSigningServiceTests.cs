using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class PluginSigningServiceTests
{
    private static PluginSigningService CreateDevService()
    {
        var config = new ConfigurationBuilder().Build();  // no keys → dev fallback
        return new PluginSigningService(config, NullLogger<PluginSigningService>.Instance);
    }

    [Fact]
    public void DevService_IsNotConfigured()
    {
        var svc = CreateDevService();
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void DevService_PublicKeyBase64_NotNull()
    {
        var svc = CreateDevService();
        Assert.NotNull(svc.PublicKeyBase64);
        Assert.NotEmpty(svc.PublicKeyBase64!);
    }

    [Fact]
    public void Sign_Then_Verify_RoundTrip()
    {
        var svc     = CreateDevService();
        var content = "manifest_version: 1\nid: test-plugin\nname: Test";

        var sig = svc.Sign(content);

        Assert.NotNull(sig);
        Assert.True(svc.Verify(content, sig));
    }

    [Fact]
    public void Verify_TamperedContent_ReturnsFalse()
    {
        var svc     = CreateDevService();
        var content = "manifest_version: 1\nid: test-plugin\nname: Test";

        var sig      = svc.Sign(content);
        var tampered = content + "\n# extra line";

        Assert.False(svc.Verify(tampered, sig));
    }

    [Fact]
    public void Verify_WrongSignature_ReturnsFalse()
    {
        var svc = CreateDevService();

        // Sign different content
        var sig = svc.Sign("different content entirely");

        Assert.False(svc.Verify("original content", sig));
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsFalse()
    {
        var svc = CreateDevService();
        Assert.False(svc.Verify("content", "not-valid-base64!!!"));
    }

    [Fact]
    public void Verify_TooShortSignature_ReturnsFalse()
    {
        var svc = CreateDevService();
        // A valid base64 string but wrong length for Ed25519
        Assert.False(svc.Verify("content", Convert.ToBase64String(new byte[16])));
    }

    [Fact]
    public void DevPublicKey_MatchesConstant()
    {
        var svc = CreateDevService();
        Assert.Equal(PluginSigningService.DevPublicKeyBase64, svc.PublicKeyBase64);
        Assert.Equal("pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=", svc.PublicKeyBase64);
    }
}
