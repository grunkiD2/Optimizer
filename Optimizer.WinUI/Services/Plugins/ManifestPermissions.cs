namespace Optimizer.WinUI.Services.Plugins;

/// <summary>
/// Allow-list of registry roots and file roots that community manifests may touch.
/// Any path outside these is rejected before execution.
/// </summary>
public static class ManifestPermissions
{
    // ── Registry allow-list ───────────────────────────────────────────────────

    /// <summary>
    /// Registry paths that plugins are allowed to modify.
    /// Paths are compared case-insensitively with StartsWith.
    /// </summary>
    private static readonly string[] AllowedRegistryPrefixes =
    {
        @"HKEY_CURRENT_USER\Software\",
        @"HKCU\Software\",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\",
        @"HKLM\SOFTWARE\Policies\",
        @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
        @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\",
        @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\",
        @"HKLM\SYSTEM\CurrentControlSet\Services\",
    };

    // ── File allow-list ───────────────────────────────────────────────────────

    /// <summary>
    /// Environment variables whose expanded values are safe file-delete/clear roots.
    /// </summary>
    private static readonly string[] AllowedFileRootEnvVars =
    {
        "TEMP", "TMP", "LOCALAPPDATA",
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the manifest registry path is within a permitted hive/sub-tree.
    /// </summary>
    public static bool IsRegistryPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return AllowedRegistryPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns <c>true</c> if the (possibly %ENV%-containing) file path expands to a
    /// permitted root (TEMP, TMP, LOCALAPPDATA, or C:\Windows\Temp).
    /// </summary>
    public static bool IsFilePathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var full = Environment.ExpandEnvironmentVariables(path);

        foreach (var ev in AllowedFileRootEnvVars)
        {
            var root = Environment.GetEnvironmentVariable(ev);
            if (!string.IsNullOrEmpty(root) &&
                full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Also allow C:\Windows\Temp explicitly
        if (full.StartsWith(@"C:\Windows\Temp", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
