using System.Text.Json;
using Microsoft.Win32;

namespace Optimizer.WinUI.Services.Data;

/// <summary>
/// Captures and restores a set of registry values as a JSON snapshot. Used by the
/// change-set layer to record concrete before/after state for an optimization so it
/// can be reverted by re-writing the captured values rather than re-running undo code.
/// </summary>
public static class RegistryStateSnapshot
{
    /// <summary>One captured registry value (or its absence).</summary>
    public sealed class ValueSnapshot
    {
        public string Root { get; set; } = "";      // "HKCU" | "HKLM"
        public string SubKey { get; set; } = "";
        public string ValueName { get; set; } = "";
        public bool Existed { get; set; }
        public string? Kind { get; set; }            // RegistryValueKind name
        public string? Value { get; set; }           // serialized (see SerializeValue)
    }

    /// <summary>Capture the current state of the given registry targets into a JSON string.</summary>
    public static string Capture(IEnumerable<(string root, string subKey, string valueName)> targets)
    {
        var snapshots = new List<ValueSnapshot>();

        foreach (var (root, subKey, valueName) in targets)
        {
            var snap = new ValueSnapshot { Root = root, SubKey = subKey, ValueName = valueName };
            using var key = OpenRoot(root)?.OpenSubKey(subKey);
            var existing = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            if (key != null && existing != null)
            {
                var kind = key.GetValueKind(valueName);
                snap.Existed = true;
                snap.Kind = kind.ToString();
                snap.Value = SerializeValue(existing, kind);
            }

            snapshots.Add(snap);
        }

        return JsonSerializer.Serialize(snapshots);
    }

    /// <summary>Restore registry values from a JSON snapshot produced by <see cref="Capture"/>.</summary>
    public static void Restore(string snapshotJson)
    {
        var snapshots = JsonSerializer.Deserialize<List<ValueSnapshot>>(snapshotJson);
        if (snapshots == null) return;

        foreach (var snap in snapshots)
        {
            using var key = OpenRoot(snap.Root)?.CreateSubKey(snap.SubKey);
            if (key == null) continue;

            if (!snap.Existed)
            {
                key.DeleteValue(snap.ValueName, throwOnMissingValue: false);
                continue;
            }

            var kind = Enum.TryParse<RegistryValueKind>(snap.Kind, out var k) ? k : RegistryValueKind.String;
            var raw = snap.Value ?? string.Empty;
            object value = kind switch
            {
                RegistryValueKind.DWord => Convert.ToInt32(raw),
                RegistryValueKind.QWord => Convert.ToInt64(raw),
                RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(raw) ?? Array.Empty<string>(),
                RegistryValueKind.Binary => Convert.FromBase64String(raw),
                _ => raw
            };
            key.SetValue(snap.ValueName, value, kind);
        }
    }

    private static string SerializeValue(object raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])raw),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])raw),
        _ => raw.ToString() ?? string.Empty
    };

    private static RegistryKey? OpenRoot(string root) => root switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        _ => null
    };
}
