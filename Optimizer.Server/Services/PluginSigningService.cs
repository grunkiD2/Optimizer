using NSec.Cryptography;
using System.Text;

namespace Optimizer.Server.Services;

/// <summary>
/// Ed25519-based signing service for plugin manifests.
///
/// Configuration (appsettings / user-secrets):
///   Plugins:SigningPrivateKey  — base64-encoded 64-byte Ed25519 private key seed+public key (NSec export)
///   Plugins:SigningPublicKey   — base64-encoded 32-byte Ed25519 public key
///
/// If neither is configured the service falls back to a hard-coded DEV keypair.
/// The dev public key is also baked into the WinUI client (PluginVerifier.DevPublicKeyBase64).
///
/// DEV KEYPAIR (do NOT use in production):
///   Private: see appsettings.Development.json  → Plugins:SigningPrivateKey
///   Public:  pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=
/// </summary>
public class PluginSigningService : IPluginSigningService
{
    // Dev keypair — generated once, hard-coded so server + client always agree in dev.
    // Production deployments MUST override via Plugins:SigningPrivateKey / Plugins:SigningPublicKey in secrets.
    //
    // To regenerate:
    //   var algo = SignatureAlgorithm.Ed25519;
    //   var key  = Key.Create(algo, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
    //   Console.WriteLine("Private: " + Convert.ToBase64String(key.Export(KeyBlobFormat.RawPrivateKey)));
    //   Console.WriteLine("Public:  " + Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)));
    internal const string DevPrivateKeyBase64 = "gfg1fH391/FnG1DSJWg/M0TIE4rN/SrCCRsh3kUxyq4=";
    internal const string DevPublicKeyBase64  = "pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=";

    private readonly Key _privateKey;
    private readonly PublicKey _publicKey;
    private readonly SignatureAlgorithm _algo = SignatureAlgorithm.Ed25519;
    private readonly bool _isConfigured;

    public PluginSigningService(IConfiguration config, ILogger<PluginSigningService> logger)
    {
        var privateKeyB64 = config["Plugins:SigningPrivateKey"];
        var publicKeyB64  = config["Plugins:SigningPublicKey"];

        if (!string.IsNullOrWhiteSpace(privateKeyB64) && !string.IsNullOrWhiteSpace(publicKeyB64))
        {
            var privBytes = Convert.FromBase64String(privateKeyB64);
            var pubBytes  = Convert.FromBase64String(publicKeyB64);
            _privateKey   = Key.Import(_algo, privBytes, KeyBlobFormat.RawPrivateKey,
                                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            _publicKey    = PublicKey.Import(_algo, pubBytes, KeyBlobFormat.RawPublicKey);
            _isConfigured = true;
            logger.LogInformation("Plugin signing: using configured production keypair. Public key: {PubKey}", publicKeyB64);
        }
        else
        {
            // Dev fallback — log a clear warning
            logger.LogWarning(
                "Plugin signing: no keys configured — using hard-coded DEV keypair. " +
                "Set Plugins:SigningPrivateKey + Plugins:SigningPublicKey in user-secrets for production. " +
                "Dev public key: {PubKey}", DevPublicKeyBase64);

            var privBytes = Convert.FromBase64String(DevPrivateKeyBase64);
            var pubBytes  = Convert.FromBase64String(DevPublicKeyBase64);
            _privateKey   = Key.Import(_algo, privBytes, KeyBlobFormat.RawPrivateKey,
                                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            _publicKey    = PublicKey.Import(_algo, pubBytes, KeyBlobFormat.RawPublicKey);
            _isConfigured = false;
        }
    }

    public bool IsConfigured => _isConfigured;
    public string? PublicKeyBase64 => Convert.ToBase64String(_publicKey.Export(KeyBlobFormat.RawPublicKey));

    public string Sign(string content)
    {
        var data = Encoding.UTF8.GetBytes(content);
        var sig  = _algo.Sign(_privateKey, data);
        return Convert.ToBase64String(sig);
    }

    public bool Verify(string content, string signatureBase64)
    {
        try
        {
            var data = Encoding.UTF8.GetBytes(content);
            var sig  = Convert.FromBase64String(signatureBase64);
            return _algo.Verify(_publicKey, data, sig);
        }
        catch
        {
            return false;
        }
    }
}
