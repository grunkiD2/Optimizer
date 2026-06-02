namespace Optimizer.WinUI.Models.Plugins;

/// <summary>
/// Top-level descriptor for a community/plugin optimization manifest.
/// Supports YAML (snake_case) and JSON deserialization via ManifestParser.
/// </summary>
public class OptimizationManifest
{
    public int ManifestVersion { get; set; } = 1;

    /// <summary>Unique identifier, e.g. "community-disable-cortana". Lowercase slug [a-z0-9-].</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";

    /// <summary>One of: Performance, Network, Storage, System, Privacy.</summary>
    public string Category { get; set; } = "System";

    public string Icon { get; set; } = "⚙️";
    public bool RequiresAdmin { get; set; } = true;
    public bool RequiresRestart { get; set; }
    public bool Reversible { get; set; } = true;

    public List<string> Pros { get; set; } = [];
    public List<string> Cons { get; set; } = [];

    public List<ManifestChange> Changes { get; set; } = [];
}

/// <summary>
/// A single declarative change inside a manifest.
/// The <see cref="Type"/> discriminates which fields are relevant.
/// </summary>
public class ManifestChange
{
    /// <summary>Change type: registry | service | file | powercfg | scheduled-task</summary>
    public string Type { get; set; } = "";

    // ── registry ─────────────────────────────────────────────────────────────

    /// <summary>Full registry path, e.g. HKLM\SOFTWARE\Policies\Microsoft\…</summary>
    public string? Path { get; set; }

    /// <summary>Registry value name.</summary>
    public string? Value { get; set; }

    /// <summary>Registry value type: dword | string | qword</summary>
    public string? ValueType { get; set; }

    /// <summary>Value to write on apply.</summary>
    public string? Apply { get; set; }

    /// <summary>Value to restore on revert, or "delete" to remove the value.</summary>
    public string? Revert { get; set; }

    // ── service ───────────────────────────────────────────────────────────────

    public string? ServiceName { get; set; }

    /// <summary>Service running state to set on apply: stopped | running</summary>
    public string? ApplyState { get; set; }

    /// <summary>Service start-up type to set on apply: disabled | manual | automatic</summary>
    public string? ApplyStartup { get; set; }

    /// <summary>Service start-up type to restore on revert: disabled | manual | automatic</summary>
    public string? RevertStartup { get; set; }

    // ── file ──────────────────────────────────────────────────────────────────

    public string? FilePath { get; set; }

    /// <summary>File action on apply: delete | clear. File changes are NOT auto-reversible.</summary>
    public string? FileAction { get; set; }

    // ── powercfg ──────────────────────────────────────────────────────────────

    /// <summary>Raw powercfg.exe arguments, e.g. "/h off". Non-trivially reversible.</summary>
    public string? PowerCfgArgs { get; set; }

    // ── scheduled-task ────────────────────────────────────────────────────────

    public string? TaskName { get; set; }

    /// <summary>Task action on apply: disable | enable</summary>
    public string? TaskAction { get; set; }
}
