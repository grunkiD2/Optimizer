namespace Optimizer.WinUI.Services.Plugins;

public record VerificationResult(bool Verified, string Reason);

/// <summary>
/// Verifies a plugin manifest's Ed25519 signature against the bundled Optimizer team public key.
/// </summary>
public interface IPluginVerifier
{
    /// <summary>
    /// Verifies <paramref name="manifestYaml"/> against <paramref name="signatureBase64"/>.
    /// Returns (true, "verified") when valid, (false, reason) otherwise.
    /// </summary>
    VerificationResult Verify(string manifestYaml, string? signatureBase64);
}
