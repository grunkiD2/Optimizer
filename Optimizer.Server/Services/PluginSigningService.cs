using NSec.Cryptography;
using System.Text;

namespace Optimizer.Server.Services;

/// <summary>
/// Ed25519-based signing service for plugin manifests.
///
/// Configuration (appsettings / user-secrets):
///   Plugins:SigningPrivateKey  — base64-encoded 32-byte Ed25519 raw private key seed
///   Plugins:SigningPublicKey   — base64-encoded 32-byte Ed25519 public key
///
/// PRIVATE KEY HANDLING:
///   The private key MUST NOT appear in source control. For local development, place the dev
///   keypair in Optimizer.Server/appsettings.Development.json (which is gitignored):
///
///     "Plugins": {
///       "SigningPrivateKey": "<base64 private seed>",
///       "SigningPublicKey":  "<base64 public key>"
///     }
///
///   For production, supply the keys via environment variables or user-secrets — never commit them.
///
///   If NO private key is configured at startup:
///     • Development environment: an ephemeral keypair is generated, logged with a loud warning.
///       Seeded plugins will be signed with this ephemeral key; the WinUI client's hard-coded
///       DevPublicKeyBase64 will NOT match, so those plugins will fail signature verification
///       unless the client also uses the ephemeral key. Use the Development config above to avoid
///       this mismatch.
///     • Production environment: an error is logged and IsConfigured = false. Sign() will throw.
///
/// The PUBLIC key constant (DevPublicKeyBase64) stays in source — it is not a secret.
/// Both the server's constant and the WinUI client's PluginVerifier.DevPublicKeyBase64 must remain
/// the same value so that dev-signed plugins verify correctly on the client.
///
/// DEV PUBLIC KEY: pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=
/// </summary>
public class PluginSigningService : IPluginSigningService
{
    // Only the PUBLIC key is safe to hard-code (it is not a secret).
    // The matching PRIVATE key lives in appsettings.Development.json (gitignored).
    internal const string DevPublicKeyBase64 = "pIn3VdAkG7MyYWAaoynpKxzFe6azFofFjJJPfSWVDcU=";

    private readonly Key? _privateKey;
    private readonly PublicKey _publicKey;
    private readonly SignatureAlgorithm _algo = SignatureAlgorithm.Ed25519;
    private readonly bool _isConfigured;

    public PluginSigningService(IConfiguration config, ILogger<PluginSigningService> logger,
        IWebHostEnvironment? env = null)
    {
        var privateKeyB64 = config["Plugins:SigningPrivateKey"];
        var publicKeyB64  = config["Plugins:SigningPublicKey"];

        if (!string.IsNullOrWhiteSpace(privateKeyB64) && !string.IsNullOrWhiteSpace(publicKeyB64))
        {
            // Configured keypair (production or dev via appsettings.Development.json / secrets).
            var privBytes = Convert.FromBase64String(privateKeyB64);
            var pubBytes  = Convert.FromBase64String(publicKeyB64);
            _privateKey   = Key.Import(_algo, privBytes, KeyBlobFormat.RawPrivateKey,
                                new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            _publicKey    = PublicKey.Import(_algo, pubBytes, KeyBlobFormat.RawPublicKey);
            _isConfigured = true;
            logger.LogInformation("Plugin signing: using configured keypair. Public key: {PubKey}", publicKeyB64);
        }
        else
        {
            bool isDevelopment = env?.IsDevelopment() ?? false;

            if (isDevelopment)
            {
                // Dev environment with no configured key: generate an ephemeral keypair.
                // WARNING: ephemeral keys change on every restart. Seeded plugins signed with this
                // key will NOT verify against the client's hard-coded DevPublicKeyBase64.
                // To avoid this, add Plugins:SigningPrivateKey + Plugins:SigningPublicKey to
                // appsettings.Development.json (gitignored).
                var ephemeralKey = Key.Create(_algo,
                    new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
                _privateKey   = ephemeralKey;
                _publicKey    = ephemeralKey.PublicKey;
                _isConfigured = true;

                var pubKeyStr = Convert.ToBase64String(_publicKey.Export(KeyBlobFormat.RawPublicKey));
                var privKeyStr = Convert.ToBase64String(_privateKey.Export(KeyBlobFormat.RawPrivateKey));
                logger.LogWarning(
                    "Plugin signing: DEV EPHEMERAL KEYPAIR generated — NOT persistent, NOT for production. " +
                    "To use a fixed dev key (recommended), add to appsettings.Development.json (gitignored): " +
                    "Plugins:SigningPrivateKey={PrivKey}  Plugins:SigningPublicKey={PubKey}",
                    privKeyStr, pubKeyStr);
            }
            else
            {
                // Production with no configured private key: signing is disabled.
                // Verification still works using the known dev public key (for marketplace integrity checks).
                var pubBytes = Convert.FromBase64String(DevPublicKeyBase64);
                _publicKey   = PublicKey.Import(_algo, pubBytes, KeyBlobFormat.RawPublicKey);
                _privateKey  = null;
                _isConfigured = false;

                logger.LogError(
                    "Plugin signing: Plugins:SigningPrivateKey is NOT configured in a non-Development environment. " +
                    "Signing is DISABLED. Set Plugins:SigningPrivateKey + Plugins:SigningPublicKey via user-secrets " +
                    "or environment variables before deploying to production.");
            }
        }
    }

    public bool IsConfigured => _isConfigured;
    public string? PublicKeyBase64 => Convert.ToBase64String(_publicKey.Export(KeyBlobFormat.RawPublicKey));

    public string Sign(string content)
    {
        if (_privateKey == null)
            throw new InvalidOperationException(
                "Plugin signing is not configured. Set Plugins:SigningPrivateKey in configuration or user-secrets.");

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
