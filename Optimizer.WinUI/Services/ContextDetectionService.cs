using System.Diagnostics;

namespace Optimizer.WinUI.Services;

/// <summary>Detects the user's current context (Gaming, Work, Plex, etc.).</summary>
public interface IContextDetectionService
{
    /// <summary>Detect the current context from running processes and time-of-day.</summary>
    Task<string> DetectContextAsync();
}

/// <summary>
/// Detects context from running processes (primary signal) and time-of-day (fallback).
/// Active-profile-based detection is a future enhancement and intentionally not wired in yet.
/// </summary>
public class ContextDetectionService : IContextDetectionService
{
    private static readonly string[] GamingProcesses = new[]
    {
        "obs64.exe", "obs.exe",           // OBS streaming
        "discord.exe",                     // Gaming chat
        "steam.exe", "steamapps",          // Steam
        "epicgames", "egs-launcher.exe",   // Epic Games
        "uplay.exe",                       // Ubisoft Play
        "origin.exe",                      // EA Origin
        "launcher.exe", "valorant.exe",    // Valorant
    };

    private static readonly string[] PlexProcesses = new[]
    {
        "plex", "plexmediaserver.exe", "plex.exe",
        "sonarr.exe", "radarr.exe",       // Plex companion apps
        "transmission.exe",                // Torrent client (often paired with Plex)
    };

    private static readonly string[] WorkProcesses = new[]
    {
        "devenv.exe",                      // Visual Studio
        "code.exe",                        // VS Code
        "rider.exe",                       // JetBrains Rider
        "outlook.exe", "thunderbird.exe",  // Email
        "teams.exe", "slack.exe",          // Collaboration
        "zoom.exe", "skype.exe",           // Conferencing
    };

    public Task<string> DetectContextAsync() => Task.FromResult(DetectByProcessesAndTime());

    private string DetectByProcessesAndTime()
    {
        var now = DateTime.Now.TimeOfDay;

        // Gaming hours: 22:00–06:00 (default assumption)
        var isGamingTime = now >= new TimeSpan(22, 0, 0) || now < new TimeSpan(6, 0, 0);
        var workTime = now >= new TimeSpan(9, 0, 0) && now < new TimeSpan(17, 0, 0);

        var runningProcesses = GetRunningProcessNames();

        // Check for specific processes
        if (runningProcesses.Any(p => GamingProcesses.Any(g => p.Contains(g, StringComparison.OrdinalIgnoreCase))))
            return "Gaming";

        if (runningProcesses.Any(p => PlexProcesses.Any(pl => p.Contains(pl, StringComparison.OrdinalIgnoreCase))))
            return "Plex";

        if (runningProcesses.Any(p => WorkProcesses.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))))
            return "Work";

        // Fallback to time-based heuristic
        if (isGamingTime)
            return "Gaming";

        if (workTime)
            return "Work";

        return "Unknown";
    }

    private static List<string> GetRunningProcessNames()
    {
        try
        {
            return Process.GetProcesses()
                .Select(p => p.ProcessName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }
}
