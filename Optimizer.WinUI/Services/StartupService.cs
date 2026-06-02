using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;

using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class StartupService : IStartupService
{
    private static readonly string BackupPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "startup-disabled.json");

    private readonly IElevationService _elevation;

    public StartupService(IElevationService elevation)
    {
        _elevation = elevation;
    }

    public List<StartupEntry> GetEntries()
    {
        var entries = new List<StartupEntry>();

        foreach (var location in new[] { StartupLocation.CurrentUser, StartupLocation.LocalMachine, StartupLocation.LocalMachineWow6432 })
        {
            using var key = OpenRunKey(location, writable: false);
            if (key == null) continue;

            foreach (var name in key.GetValueNames().Where(n => !string.IsNullOrEmpty(n)))
            {
                var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (raw == null) continue;

                entries.Add(new StartupEntry
                {
                    Name = name,
                    Command = raw.ToString() ?? string.Empty,
                    Location = location,
                    ValueKind = key.GetValueKind(name).ToString(),
                    Enabled = true
                });
            }
        }

        // Append entries we previously disabled (they no longer live in the Run keys).
        foreach (var disabled in LoadBackup())
        {
            if (!entries.Any(e => e.Name == disabled.Name && e.Location == disabled.Location))
            {
                disabled.Enabled = false;
                entries.Add(disabled);
            }
        }

        // Startup-folder shortcuts (enabled = in the folder; disabled = in our "Disabled" subfolder).
        foreach (var (location, folder) in StartupFolders())
        {
            if (folder == null) continue;
            if (Directory.Exists(folder))
            {
                foreach (var lnk in Directory.GetFiles(folder, "*.lnk"))
                {
                    entries.Add(new StartupEntry { Name = Path.GetFileNameWithoutExtension(lnk), Command = lnk, Location = location, Enabled = true });
                }
            }
            var disabledDir = Path.Combine(folder, "Disabled");
            if (Directory.Exists(disabledDir))
            {
                foreach (var lnk in Directory.GetFiles(disabledDir, "*.lnk"))
                {
                    entries.Add(new StartupEntry { Name = Path.GetFileNameWithoutExtension(lnk), Command = lnk, Location = location, Enabled = false });
                }
            }
        }

        entries.AddRange(GetLogonTasks());
        entries.AddRange(GetAutoServices());

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------------------------------------------------- Scheduled tasks

    private static List<StartupEntry> GetLogonTasks()
    {
        var result = new List<StartupEntry>();
        try
        {
            using var ts = new TaskService();
            CollectLogonTasks(ts.RootFolder, result);
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error enumerating scheduled tasks: {ex.Message}");
        }
        return result;
    }

    private static void CollectLogonTasks(TaskFolder folder, List<StartupEntry> into)
    {
        // Skip OS tasks under \Microsoft to avoid users disabling critical system jobs.
        if (folder.Path.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase)) return;

        foreach (var task in folder.Tasks)
        {
            try
            {
                if (task.Definition.Triggers.Any(t => t.TriggerType == TaskTriggerType.Logon))
                {
                    into.Add(new StartupEntry
                    {
                        Name = task.Name,
                        Command = task.Definition.Actions.FirstOrDefault()?.ToString() ?? task.Path,
                        Key = task.Path,
                        Location = StartupLocation.ScheduledTask,
                        Enabled = task.Enabled
                    });
                }
            }
            catch { /* skip unreadable task */ }
        }

        foreach (var sub in folder.SubFolders)
        {
            CollectLogonTasks(sub, into);
        }
    }

    private static void SetTaskEnabled(StartupEntry entry, bool enabled)
    {
        using var ts = new TaskService();
        var task = ts.GetTask(entry.Key);
        if (task != null)
        {
            task.Enabled = enabled;
        }
    }

    // ---------------------------------------------------------------- Services

    private static readonly string ServicesManagedPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "services-managed.json");

    private static List<StartupEntry> GetAutoServices()
    {
        var result = new List<StartupEntry>();
        var managed = LoadManagedServices();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, PathName, StartMode FROM Win32_Service WHERE StartMode='Auto' OR StartMode='Manual'");
            foreach (ManagementObject mo in searcher.Get())
            {
                var name = mo["Name"]?.ToString() ?? string.Empty;
                var mode = mo["StartMode"]?.ToString() ?? string.Empty;
                var isAuto = string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase);

                // Show all Auto services (enabled) plus services we set to Manual (disabled).
                if (!isAuto && !managed.Contains(name)) continue;

                result.Add(new StartupEntry
                {
                    Name = mo["DisplayName"]?.ToString() ?? name,
                    Command = mo["PathName"]?.ToString() ?? string.Empty,
                    Key = name,
                    Location = StartupLocation.Service,
                    Enabled = isAuto
                });
            }
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error enumerating services: {ex.Message}");
        }
        return result;
    }

    private void SetServiceEnabled(StartupEntry entry, bool enabled)
    {
        // Auto <-> Manual (never Disabled) so the service stays available on demand — safer.
        RunSc($"config \"{entry.Key}\" start= {(enabled ? "auto" : "demand")}");

        var managed = LoadManagedServices();
        if (enabled) managed.Remove(entry.Key); else managed.Add(entry.Key);
        SaveManagedServices(managed);
    }

    private static void RunSc(string arguments)
    {
        using var p = Process.Start(new ProcessStartInfo("sc", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
        p?.WaitForExit(8000);
    }

    private static HashSet<string> LoadManagedServices()
    {
        try
        {
            if (File.Exists(ServicesManagedPath))
            {
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(ServicesManagedPath))
                       ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { /* ignore */ }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static void SaveManagedServices(HashSet<string> set)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ServicesManagedPath)!);
            File.WriteAllText(ServicesManagedPath, JsonSerializer.Serialize(set));
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Error saving managed services: {ex.Message}");
        }
    }

    private static IEnumerable<(StartupLocation, string?)> StartupFolders()
    {
        yield return (StartupLocation.StartupFolderUser, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
        yield return (StartupLocation.StartupFolderCommon, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
    }

    private static string? StartupFolderPath(StartupLocation location) => location switch
    {
        StartupLocation.StartupFolderUser => Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        StartupLocation.StartupFolderCommon => Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
        _ => null
    };

    private static void SetFolderEntryEnabled(StartupEntry entry, bool enabled)
    {
        var folder = StartupFolderPath(entry.Location);
        if (folder == null) return;

        var fileName = entry.Name + ".lnk";
        var enabledPath = Path.Combine(folder, fileName);
        var disabledDir = Path.Combine(folder, "Disabled");
        var disabledPath = Path.Combine(disabledDir, fileName);

        if (enabled)
        {
            if (File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath, overwrite: true);
            }
        }
        else
        {
            Directory.CreateDirectory(disabledDir);
            if (File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath, overwrite: true);
            }
        }
    }

    public bool SetEnabled(StartupEntry entry, bool enabled)
    {
        if (entry.RequiresAdmin && !_elevation.IsElevated)
        {
            return false;
        }

        try
        {
            if (entry.Location is StartupLocation.StartupFolderUser or StartupLocation.StartupFolderCommon)
            {
                SetFolderEntryEnabled(entry, enabled);
            }
            else if (entry.Location == StartupLocation.ScheduledTask)
            {
                SetTaskEnabled(entry, enabled);
            }
            else if (entry.Location == StartupLocation.Service)
            {
                SetServiceEnabled(entry, enabled);
            }
            else if (enabled)
            {
                RestoreEntry(entry);
            }
            else
            {
                DisableEntry(entry);
            }
            entry.Enabled = enabled;
            return true;
        }
        catch (Exception ex)
        {
            EngineLog.Write($"SetEnabled failed for '{entry.Name}': {ex.Message}");
            return false;
        }
    }

    private void DisableEntry(StartupEntry entry)
    {
        using (var key = OpenRunKey(entry.Location, writable: true))
        {
            key?.DeleteValue(entry.Name, throwOnMissingValue: false);
        }

        var backup = LoadBackup();
        if (!backup.Any(e => e.Name == entry.Name && e.Location == entry.Location))
        {
            backup.Add(entry);
            SaveBackup(backup);
        }
    }

    private void RestoreEntry(StartupEntry entry)
    {
        using (var key = OpenRunKey(entry.Location, writable: true))
        {
            if (key != null)
            {
                var kind = Enum.TryParse<RegistryValueKind>(entry.ValueKind, out var k) ? k : RegistryValueKind.String;
                key.SetValue(entry.Name, entry.Command, kind);
            }
        }

        var backup = LoadBackup();
        backup.RemoveAll(e => e.Name == entry.Name && e.Location == entry.Location);
        SaveBackup(backup);
    }

    private static RegistryKey? OpenRunKey(StartupLocation location, bool writable)
    {
        const string runPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string wowRunPath = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run";

        return location switch
        {
            StartupLocation.CurrentUser => Registry.CurrentUser.CreateSubKey(runPath, writable),
            StartupLocation.LocalMachine => writable
                ? Registry.LocalMachine.OpenSubKey(runPath, writable: true)
                : Registry.LocalMachine.OpenSubKey(runPath),
            StartupLocation.LocalMachineWow6432 => writable
                ? Registry.LocalMachine.OpenSubKey(wowRunPath, writable: true)
                : Registry.LocalMachine.OpenSubKey(wowRunPath),
            _ => null
        };
    }

    private static List<StartupEntry> LoadBackup()
    {
        try
        {
            if (!File.Exists(BackupPath)) return new List<StartupEntry>();
            var json = File.ReadAllText(BackupPath);
            return JsonSerializer.Deserialize<List<StartupEntry>>(json) ?? new List<StartupEntry>();
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Failed to load startup backup: {ex.Message}");
            return new List<StartupEntry>();
        }
    }

    private static void SaveBackup(List<StartupEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupPath)!);
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            EngineLog.Write($"Failed to save startup backup: {ex.Message}");
        }
    }
}
