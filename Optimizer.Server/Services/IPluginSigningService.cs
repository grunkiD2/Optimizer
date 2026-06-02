namespace Optimizer.Server.Services;

public interface IPluginSigningService
{
    bool IsConfigured { get; }
    string? PublicKeyBase64 { get; }

    /// <summary>Signs the UTF-8 bytes of <paramref name="content"/> and returns a base64 Ed25519 signature.</summary>
    string Sign(string content);

    /// <summary>Verifies that <paramref name="signatureBase64"/> is a valid Ed25519 signature over <paramref name="content"/>.</summary>
    bool Verify(string content, string signatureBase64);
}
