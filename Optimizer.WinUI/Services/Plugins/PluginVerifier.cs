using NSec.Cryptography;
using System.Text;

namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Ed25519 signature verifier for plugin manifests.
///
/// The dev public key matches the server's hard-coded dev private key in PluginSigningService.
/// Production deployments use the real Optimizer team public key — update DevPublicKeyBase64
/// (or inject via config) when deploying with production keys.
/// </summary>
public class PluginVerifier : IPluginVerifier
{
    /// <summary>
    /// The dev public key baked into the client.
    /// Matches PluginSigningService.DevPublicKeyBase64 on the server.
    /// </summary>
    public const string DevPublicKeyBase64 = "pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=";

    private readonly SignatureAlgorithm _algo = SignatureAlgorithm.Ed25519;
    private readonly PublicKey _publicKey;

    public PluginVerifier()
    {
        var pubBytes = Convert.FromBase64String(DevPublicKeyBase64);
        _publicKey   = PublicKey.Import(_algo, pubBytes, KeyBlobFormat.RawPublicKey);
    }

    public VerificationResult Verify(string manifestYaml, string? signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64))
            return new VerificationResult(false, "unsigned (community plugin)");

        try
        {
            var data = Encoding.UTF8.GetBytes(manifestYaml);
            var sig  = Convert.FromBase64String(signatureBase64);
            var ok   = _algo.Verify(_publicKey, data, sig);
            return ok
                ? new VerificationResult(true,  "verified by Optimizer team")
                : new VerificationResult(false, "signature invalid");
        }
        catch (Exception ex)
        {
            return new VerificationResult(false, $"verification error: {ex.Message}");
        }
    }
}
