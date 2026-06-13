namespace Optimizer.WinUI.Services;

public enum UndoActionKind
{
    RegistryValue,
    ActivePowerScheme
}

/// <summary>A single reversible change captured before an optimization mutated the system.</summary>
public class UndoEntry
{
    public UndoActionKind Kind { get; set; }
    public string Description { get; set; } = string.Empty;
    /// <summary>The optimization/profile id that produced this entry (audit C5/C12 — exact-match
    /// undo instead of the fragile Description.Contains(id) that never matched). Null for
    /// pre-audit entries loaded from an older undo.json.</summary>
    public string? OptimizationId { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    // Registry-value undo
    public string? RegistryRoot { get; set; }      // "HKCU" or "HKLM"
    public string? SubKey { get; set; }
    public string? ValueName { get; set; }
    public bool ValueExisted { get; set; }
    public string? PreviousValueKind { get; set; }  // RegistryValueKind name
    public string? PreviousValue { get; set; }       // string-serialized prior value

    // Power-scheme undo
    public string? PreviousPowerSchemeGuid { get; set; }
}

public interface IUndoService
{
    int Count { get; }
    IReadOnlyList<UndoEntry> Entries { get; }

    /// <summary>Reads the current registry value (if any) and records how to restore it, then returns.</summary>
    void CaptureRegistry(string root, string subKey, string valueName, string description, string? optimizationId = null);

    /// <summary>Records the currently-active power scheme so it can be restored later.</summary>
    void CapturePowerScheme(string previousGuid, string description, string? optimizationId = null);

    /// <summary>Reverts every captured change, most recent first. Returns the number restored.</summary>
    Task<int> UndoAllAsync();

    /// <summary>Reverts a single captured change and removes it from the log. Returns true if reverted.</summary>
    Task<bool> UndoAsync(UndoEntry entry);

    void Load();
    Task SaveAsync();
}
