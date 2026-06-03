using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

        using (var fs = File.Create(_file, 4096, FileOptions.SequentialScan))
        {
            var bytes = Encoding.UTF8.GetBytes(Convert.ToBase64String(protectedBytes));
            fs.Write(bytes, 0, bytes.Length);
        }

        SetFilePermissionsCurrentUserOnly(_file);
    }

    private static void SetFilePermissionsCurrentUserOnly(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileSecurity = fileInfo.GetAccessControl();

            fileSecurity.SetAccessRuleProtection(true, false);
            fileSecurity.RemoveAccessRuleAll(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.FullControl, AccessControlType.Allow));

            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                var rule = new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.Read | FileSystemRights.Write | FileSystemRights.Delete,
                    AccessControlType.Allow);
                fileSecurity.SetAccessRule(rule);
                fileInfo.SetAccessControl(fileSecurity);
            }
        }
        catch { }
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
