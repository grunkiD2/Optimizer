namespace Optimizer.WinUI.Models;

public enum StartupLocation
{
    CurrentUser,          // HKCU\…\Run
    LocalMachine,         // HKLM\…\Run
    LocalMachineWow6432,  // HKLM\…\Wow6432Node\…\Run (32-bit on 64-bit Windows)
    StartupFolderUser,    // %AppData%\…\Startup
    StartupFolderCommon,  // %ProgramData%\…\Startup (all users)
    ScheduledTask,        // logon-triggered Task Scheduler job
    Service               // automatic-start Windows service
}

/// <summary>A single auto-start entry from a registry Run key.</summary>
public class StartupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;

    /// <summary>Stable identifier used to toggle the entry (service short name or task path); falls back to Name.</summary>
    public string Key { get; set; } = string.Empty;

    public StartupLocation Location { get; set; }

    /// <summary>Registry value kind name (e.g. "String", "ExpandString") so it round-trips exactly.</summary>
    public string ValueKind { get; set; } = "String";

    /// <summary>True when the entry currently launches at sign-in.</summary>
    public bool Enabled { get; set; }

    /// <summary>Machine-wide locations require an elevated process to modify.</summary>
    public bool RequiresAdmin => Location is StartupLocation.LocalMachine
        or StartupLocation.LocalMachineWow6432
        or StartupLocation.StartupFolderCommon
        or StartupLocation.ScheduledTask
        or StartupLocation.Service;

    public string LocationText => Location switch
    {
        StartupLocation.CurrentUser => "Run (current user)",
        StartupLocation.LocalMachine => "Run (all users · admin)",
        StartupLocation.LocalMachineWow6432 => "Run (all users · 32-bit · admin)",
        StartupLocation.StartupFolderUser => "Startup folder (user)",
        StartupLocation.StartupFolderCommon => "Startup folder (all users · admin)",
        StartupLocation.ScheduledTask => "Scheduled task (admin)",
        StartupLocation.Service => "Service · auto-start (admin)",
        _ => Location.ToString()
    };
}

/// <summary>A captured on/off state for a startup entry, stored in a profile so it can be restored.</summary>
public class StartupState
{
    public string Name { get; set; } = string.Empty;
    public StartupLocation Location { get; set; }
    public bool Enabled { get; set; }
}
