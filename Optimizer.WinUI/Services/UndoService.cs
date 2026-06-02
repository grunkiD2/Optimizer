using System.Diagnostics;
using System.IO;
using System.Text.Json;

using Microsoft.Win32;

namespace Optimizer.WinUI.Services;

public class UndoService : IUndoService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "undo.json");

    private readonly List<UndoEntry> _entries = new();
    private readonly object _gate = new();

    public int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }

    public IReadOnlyList<UndoEntry> Entries
    {
        get { lock (_gate) { return _entries.ToArray(); } }
    }

    public void CaptureRegistry(string root, string subKey, string valueName, string description)
    {
        using var key = OpenRoot(root, writable: false)?.OpenSubKey(subKey);

        var entry = new UndoEntry
        {
            Kind = UndoActionKind.RegistryValue,
            Description = description,
            RegistryRoot = root,
            SubKey = subKey,
            ValueName = valueName,
            ValueExisted = false
        };

        // Read the prior value without expanding %ENV% tokens so ExpandString round-trips exactly.
        var existing = key?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (key != null && existing != null)
        {
            var kind = key.GetValueKind(valueName);
            entry.ValueExisted = true;
            entry.PreviousValueKind = kind.ToString();
            entry.PreviousValue = SerializeValue(existing, kind);
        }

        lock (_gate) { _entries.Add(entry); }
    }

    private static string SerializeValue(object raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.MultiString => JsonSerializer.Serialize((string[])raw),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])raw),
        _ => raw.ToString() ?? string.Empty
    };

    public void CapturePowerScheme(string previousGuid, string description)
    {
        lock (_gate)
        {
            _entries.Add(new UndoEntry
            {
                Kind = UndoActionKind.ActivePowerScheme,
                Description = description,
                PreviousPowerSchemeGuid = previousGuid
            });
        }
    }

    public async Task<int> UndoAllAsync()
    {
        UndoEntry[] toRevert;
        lock (_gate)
        {
            toRevert = _entries.AsEnumerable().Reverse().ToArray();
            _entries.Clear();
        }

        var restored = 0;
        foreach (var entry in toRevert)
        {
            try
            {
                switch (entry.Kind)
                {
                    case UndoActionKind.RegistryValue:
                        RestoreRegistry(entry);
                        restored++;
                        break;
                    case UndoActionKind.ActivePowerScheme:
                        if (!string.IsNullOrEmpty(entry.PreviousPowerSchemeGuid))
                        {
                            RunPowerCfg($"/setactive {entry.PreviousPowerSchemeGuid}");
                            restored++;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                EngineLog.Write($"Undo failed for '{entry.Description}': {ex.Message}");
            }
        }

        await SaveAsync();
        return restored;
    }

    public async Task<bool> UndoAsync(UndoEntry entry)
    {
        bool removed;
        lock (_gate) { removed = _entries.Remove(entry); }
        if (!removed)
        {
            return false;
        }

        try
        {
            switch (entry.Kind)
            {
                case UndoActionKind.RegistryValue:
                    RestoreRegistry(entry);
                    break;
                case UndoActionKind.ActivePowerScheme:
                    if (!string.IsNullOrEmpty(entry.PreviousPowerSchemeGuid))
                    {
                        RunPowerCfg($"/setactive {entry.PreviousPowerSchemeGuid}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Undo failed for '{entry.Description}'", ex);
            await SaveAsync();
            return false;
        }

        await SaveAsync();
        return true;
    }

    private static void RestoreRegistry(UndoEntry entry)
    {
        if (entry.RegistryRoot == null || entry.SubKey == null || entry.ValueName == null)
        {
            return;
        }

        using var key = OpenRoot(entry.RegistryRoot, writable: true)?.CreateSubKey(entry.SubKey);
        if (key == null)
        {
            return;
        }

        if (!entry.ValueExisted)
        {
            key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
            return;
        }

        var kind = Enum.TryParse<RegistryValueKind>(entry.PreviousValueKind, out var k) ? k : RegistryValueKind.String;
        var prev = entry.PreviousValue ?? string.Empty;
        object value = kind switch
        {
            RegistryValueKind.DWord => Convert.ToInt32(prev),
            RegistryValueKind.QWord => Convert.ToInt64(prev),
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(prev) ?? Array.Empty<string>(),
            RegistryValueKind.Binary => Convert.FromBase64String(prev),
            // String and ExpandString both restore from the raw (unexpanded) string.
            _ => prev
        };
        key.SetValue(entry.ValueName, value, kind);
    }

    private static RegistryKey? OpenRoot(string root, bool writable) => root switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        _ => null
    };

    internal static void RunPowerCfg(string arguments)
    {
        var psi = new ProcessStartInfo("powercfg", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        process?.WaitForExit(5000);
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return;
            }

            var json = File.ReadAllText(StorePath);
            var loaded = JsonSerializer.Deserialize<List<UndoEntry>>(json);
            if (loaded != null)
            {
                lock (_gate)
                {
                    _entries.Clear();
                    _entries.AddRange(loaded);
                }
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Failed to load undo log: {ex.Message}");
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            UndoEntry[] snapshot;
            lock (_gate) { snapshot = _entries.ToArray(); }
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(StorePath, json);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Failed to save undo log: {ex.Message}");
        }
    }
}
