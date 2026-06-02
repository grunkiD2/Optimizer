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
    /// Returns <c>true</c> if the (possibly %ENV%-containing) file path expands to a
    /// permitted root (TEMP, TMP, LOCALAPPDATA, or C:\Windows\Temp).
    ///
    /// Both the candidate path and each allowed root are canonicalized with
    /// <see cref="Path.GetFullPath"/> before comparison, so traversal sequences such as
    /// <c>%TEMP%\..\..\..\Windows\System32\...</c> are resolved to their real paths and
    /// correctly rejected if they fall outside the allowed roots.
    /// </summary>
    public static bool IsFilePathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        string full;
        try
        {
            // ExpandEnvironmentVariables then GetFullPath resolves '..' segments,
            // eliminating path-traversal attacks like %TEMP%\..\..\..\Windows\System32\...
            full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch
        {
            // Invalid path (e.g. contains illegal characters) → reject
            return false;
        }

        foreach (var ev in AllowedFileRootEnvVars)
        {
            var rootRaw = Environment.GetEnvironmentVariable(ev);
            if (string.IsNullOrEmpty(rootRaw)) continue;

            string rootFull;
            try { rootFull = Path.GetFullPath(rootRaw); }
            catch { continue; }

            // Ensure the path is strictly inside the root (not just equal to it)
            // by requiring a directory separator after the root prefix.
            var rootWithSep = rootFull.TrimEnd(Path.DirectorySeparatorChar,
                                                Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;

            if (full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Also allow C:\Windows\Temp explicitly (canonicalized)
        try
        {
            var winTemp = Path.GetFullPath(@"C:\Windows\Temp") + Path.DirectorySeparatorChar;
            if (full.StartsWith(winTemp, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { /* ignore */ }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the registry path is within a permitted hive/sub-tree
    /// AND does not contain any '.' or '..' traversal segments in the subkey portion.
    /// </summary>
    public static bool IsRegistryPathAllowed(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // Reject any path segment that is '.' or '..' (registry traversal via CreateSubKey)
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // Skip the root element (e.g. "HKCU" / "HKEY_CURRENT_USER"); check the rest
        if (segments.Skip(1).Any(s => s is "." or ".."))
            return false;

        return AllowedRegistryPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
