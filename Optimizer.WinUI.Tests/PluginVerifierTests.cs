using System;
using System.Text;
using NSec.Cryptography;
using Optimizer.WinUI.Services.Plugins;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Pure crypto tests for PluginVerifier — no shared state, no collection needed.
/// </summary>
public class PluginVerifierTests
{
    private readonly PluginVerifier _verifier = new();
    private const string SampleManifest =
        "manifest_version: 1\nid: test\nname: Test Plugin\ndescription: A test.\ncategory: Privacy\nchanges: []";

    // ── Helper: sign a string with the dev private key ────────────────────────

    private static string SignWithDevKey(string content)
    {
        var algo     = SignatureAlgorithm.Ed25519;
        var privBytes = Convert.FromBase64String(
            "gfg1fH391/FnG1DSJWg/M0TIE4rN/SrCCRsh3kUxyq4=");  // matches PluginSigningService.DevPrivateKeyBase64
        var key = Key.Import(algo, privBytes, KeyBlobFormat.RawPrivateKey,
            new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var sig = algo.Sign(key, Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(sig);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Verify_ValidSignature_ReturnsVerified()
    {
        var sig    = SignWithDevKey(SampleManifest);
        var result = _verifier.Verify(SampleManifest, sig);

        Assert.True(result.Verified);
        Assert.Contains("verified", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_NullSignature_ReturnsUnsigned()
    {
        var result = _verifier.Verify(SampleManifest, null);

        Assert.False(result.Verified);
        Assert.Contains("unsigned", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_EmptySignature_ReturnsUnsigned()
    {
        var result = _verifier.Verify(SampleManifest, "");

        Assert.False(result.Verified);
        Assert.Contains("unsigned", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_TamperedContent_ReturnsInvalid()
    {
        var sig      = SignWithDevKey(SampleManifest);
        var tampered = SampleManifest + "\n# tampered";

        var result = _verifier.Verify(tampered, sig);

        Assert.False(result.Verified);
        Assert.Contains("invalid", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_WrongSignature_ReturnsInvalid()
    {
        // Sign a completely different string, then verify against SampleManifest
        var wrongSig = SignWithDevKey("some other content");

        var result = _verifier.Verify(SampleManifest, wrongSig);

        Assert.False(result.Verified);
        Assert.Contains("invalid", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_InvalidBase64_ReturnsFalse()
    {
        var result = _verifier.Verify(SampleManifest, "not!valid!base64!!!");

        Assert.False(result.Verified);
    }

    [Fact]
    public void DevPublicKeyConstant_MatchesExpected()
    {
        // Ensure the client constant hasn't drifted from the expected value
        Assert.Equal("pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=", PluginVerifier.DevPublicKeyBase64);
    }
}
