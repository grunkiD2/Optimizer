using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Optimizer.Server.Services;
using Xunit;

namespace Optimizer.Server.Tests;

public class PluginSigningServiceTests
{
    /// <summary>
    /// Creates a service in development mode with no config → gets ephemeral keypair.
    /// IsConfigured = true (ephemeral key was generated).
    /// </summary>
    private static PluginSigningService CreateDevEphemeralService()
    {
        var config = new ConfigurationBuilder().Build();  // no keys configured
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        return new PluginSigningService(config, NullLogger<PluginSigningService>.Instance, env.Object);
    }

    /// <summary>
    /// Creates a service with explicit keypair in config (simulates appsettings.Development.json or secrets).
    /// </summary>
    private static PluginSigningService CreateConfiguredService()
    {
        // Use the fixed dev keypair — private key is loaded from config, never from a source constant
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plugins:SigningPrivateKey"] = "gfg1fH391/FnG1DSJWg/M0TIE4rN/SrCCRsh3kUxyq4=",
                ["Plugins:SigningPublicKey"]  = PluginSigningService.DevPublicKeyBase64,
            })
            .Build();
        return new PluginSigningService(config, NullLogger<PluginSigningService>.Instance);
    }

    /// <summary>
    /// Creates a service in production mode with no config → signing disabled.
    /// </summary>
    private static PluginSigningService CreateProductionUnconfiguredService()
    {
        var config = new ConfigurationBuilder().Build();  // no keys configured
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        return new PluginSigningService(config, NullLogger<PluginSigningService>.Instance, env.Object);
    }

    // ── Dev ephemeral service ─────────────────────────────────────────────────

    [Fact]
    public void DevEphemeral_IsConfigured_True()
    {
        // Ephemeral key is generated; signing is enabled in dev
        var svc = CreateDevEphemeralService();
        Assert.True(svc.IsConfigured);
    }

    [Fact]
    public void DevEphemeral_PublicKeyBase64_NotNull()
    {
        var svc = CreateDevEphemeralService();
        Assert.NotNull(svc.PublicKeyBase64);
        Assert.NotEmpty(svc.PublicKeyBase64!);
    }

    [Fact]
    public void DevEphemeral_Sign_Then_Verify_RoundTrip()
    {
        var svc     = CreateDevEphemeralService();
        var content = "manifest_version: 1\nid: test-plugin\nname: Test";

        var sig = svc.Sign(content);

        Assert.NotNull(sig);
        Assert.True(svc.Verify(content, sig));
    }

    [Fact]
    public void DevEphemeral_Verify_TamperedContent_ReturnsFalse()
    {
        var svc     = CreateDevEphemeralService();
        var content = "manifest_version: 1\nid: test-plugin\nname: Test";

        var sig      = svc.Sign(content);
        var tampered = content + "\n# extra line";

        Assert.False(svc.Verify(tampered, sig));
    }

    // ── Configured (production-style) service ─────────────────────────────────

    [Fact]
    public void Configured_IsConfigured_True()
    {
        var svc = CreateConfiguredService();
        Assert.True(svc.IsConfigured);
    }

    [Fact]
    public void Configured_Sign_Then_Verify_RoundTrip()
    {
        var svc     = CreateConfiguredService();
        var content = "manifest_version: 1\nid: test-plugin\nname: Test";

        var sig = svc.Sign(content);

        Assert.NotNull(sig);
        Assert.True(svc.Verify(content, sig));
    }

    [Fact]
    public void Configured_PublicKey_MatchesDevPublicKeyConstant()
    {
        // The configured service loaded the known dev keypair → public key must match the constant
        var svc = CreateConfiguredService();
        Assert.Equal(PluginSigningService.DevPublicKeyBase64, svc.PublicKeyBase64);
        Assert.Equal("pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=", svc.PublicKeyBase64);
    }

    // ── Production unconfigured service ──────────────────────────────────────

    [Fact]
    public void Production_Unconfigured_IsConfigured_False()
    {
        var svc = CreateProductionUnconfiguredService();
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public void Production_Unconfigured_Sign_Throws()
    {
        var svc = CreateProductionUnconfiguredService();
        Assert.Throws<InvalidOperationException>(() => svc.Sign("anything"));
    }

    [Fact]
    public void Production_Unconfigured_Verify_StillWorks()
    {
        // Verification uses the hard-coded public key even when signing is disabled
        var configuredSvc = CreateConfiguredService();
        var prodSvc       = CreateProductionUnconfiguredService();
        var content       = "manifest_version: 1\nid: test";

        var sig = configuredSvc.Sign(content);
        Assert.True(prodSvc.Verify(content, sig));
    }

    // ── Shared negative cases ─────────────────────────────────────────────────

    [Fact]
    public void Verify_WrongSignature_ReturnsFalse()
    {
        var svc = CreateConfiguredService();

        var sig = svc.Sign("different content entirely");
        Assert.False(svc.Verify("original content", sig));
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsFalse()
    {
        var svc = CreateConfiguredService();
        Assert.False(svc.Verify("content", "not-valid-base64!!!"));
    }

    [Fact]
    public void Verify_TooShortSignature_ReturnsFalse()
    {
        var svc = CreateConfiguredService();
        Assert.False(svc.Verify("content", Convert.ToBase64String(new byte[16])));
    }

    // ── Security: private key NOT in source ───────────────────────────────────

    [Fact]
    public void NoDevPrivateKeyBase64_ConstantDoesNotExist()
    {
        // Verify the compile-time constant DevPrivateKeyBase64 no longer exists.
        // If this test fails to compile, the constant was re-added to source — that is the exploit.
        var publicOnly = typeof(PluginSigningService)
            .GetField("DevPrivateKeyBase64",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.Null(publicOnly);
    }
}
