using System.Security.Cryptography;
using System.Text;
using Optimizer.WinUI.Helpers;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Stores the Anthropic API key encrypted with Windows DPAPI (CurrentUser scope).</summary>
public sealed class DpapiApiKeyStore : IApiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Optimizer.Assistant.ApiKey.v1");
    private readonly string _file;

    /// <summary>Production ctor — stores under %LocalAppData%\Optimizer\.</summary>
    public DpapiApiKeyStore() : this(AppPaths.GetDataFile("assistant-api-key.bin")) { }

    /// <summary>Test ctor — explicit path.</summary>
    public DpapiApiKeyStore(string file) => _file = file;

    public bool HasKey => File.Exists(_file);

    public void SetKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { Clear(); return; }
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, Convert.ToBase64String(protectedBytes));
    }

    public string? GetKey()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var protectedBytes = Convert.FromBase64String(File.ReadAllText(_file));
            var clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clear);
        }
        catch { return null; }
    }

    public void Clear()
    {
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }
}
