using System.Diagnostics;
using Microsoft.Win32;

namespace Optimizer.WinUI.Services;

/// <summary>
/// The user's declared setup intent, as captured by Windows at first-run / Settings → Personalization.
/// Stored as a bitmask at HKCU\Software\Microsoft\Windows\CurrentVersion\CloudExperienceHost\Intent.
/// </summary>
public sealed record UserIntent(
    bool Gaming,
    bool Family,
    bool Creativity,
    bool Schoolwork,
    bool Entertainment,
    bool Business,
    bool Development,
    bool DevModeEnabled)
{
    public static readonly UserIntent None =
        new(false, false, false, false, false, false, false, false);

    /// <summary>Human-readable summary, suitable for the assistant's context block.</summary>
    public string ToPromptHint()
    {
        var bits = new List<string>(8);
        if (Gaming) bits.Add("Gaming");
        if (Family) bits.Add("Family");
        if (Creativity) bits.Add("Creativity");
        if (Schoolwork) bits.Add("Schoolwork");
        if (Entertainment) bits.Add("Entertainment");
        if (Business) bits.Add("Business");
        if (Development) bits.Add("Development");
        if (DevModeEnabled) bits.Add("DevMode");
        return bits.Count == 0 ? "none declared" : string.Join(", ", bits);
    }
}

/// <summary>Detects the user's current context (Gaming, Work, Plex, etc.).</summary>
public interface IContextDetectionService
{
    /// <summary>Detect the current context from running processes, time-of-day, and declared user intent.</summary>
    Task<string> DetectContextAsync();

    /// <summary>The user's declared setup intent from Windows. Cached at construction.</summary>
    UserIntent UserIntent { get; }
}

/// <summary>
/// R4: the raw process/time/intent GUESS — only ContextAuthorityService should consume this.
/// suppressGaming skips every Gaming-producing branch (process list, intent bit, night-hours
/// fallback) so the authority can ask "what else could this be?" once the machine's measured
/// state has already ruled gaming out.
/// </summary>
public interface IContextGuesser
{
    Task<string> GuessContextAsync(bool suppressGaming);
    UserIntent UserIntent { get; }
}

/// <summary>
/// Detects context from running processes (primary signal), time-of-day (fallback),
/// and the Windows-stored "user intent" bitmask (bias). The intent bitmask never overrides
/// a confident process match — it only breaks ties when processes are ambiguous, and it
/// flows through to the assistant so prompts can reference the user's declared setup.
/// </summary>
public class ContextDetectionService : IContextDetectionService, IContextGuesser
{
    public UserIntent UserIntent { get; }

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

    public ContextDetectionService()
    {
        UserIntent = ReadUserIntent();
    }

    public Task<string> DetectContextAsync() => Task.FromResult(DetectByProcessesAndTime(suppressGaming: false));

    public Task<string> GuessContextAsync(bool suppressGaming) => Task.FromResult(DetectByProcessesAndTime(suppressGaming));

    private string DetectByProcessesAndTime(bool suppressGaming)
    {
        var now = DateTime.Now.TimeOfDay;

        // Gaming hours: 22:00–06:00 (default assumption)
        var isGamingTime = now >= new TimeSpan(22, 0, 0) || now < new TimeSpan(6, 0, 0);
        var workTime = now >= new TimeSpan(9, 0, 0) && now < new TimeSpan(17, 0, 0);

        var runningProcesses = GetRunningProcessNames();

        // Process-based detection (primary signal — most authoritative).
        if (!suppressGaming &&
            runningProcesses.Any(p => GamingProcesses.Any(g => p.Contains(g, StringComparison.OrdinalIgnoreCase))))
            return "Gaming";

        if (runningProcesses.Any(p => PlexProcesses.Any(pl => p.Contains(pl, StringComparison.OrdinalIgnoreCase))))
            return "Plex";

        if (runningProcesses.Any(p => WorkProcesses.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))))
            return "Work";

        // No confident process match — try declared user intent before falling through
        // to time-of-day. Map each intent bit to the closest existing context.
        var intentContext = MapUserIntentToContext(UserIntent, suppressGaming);
        if (intentContext is not null)
            return intentContext;

        // Fallback to time-based heuristic
        if (isGamingTime && !suppressGaming)
            return "Gaming";

        if (workTime)
            return "Work";

        return "Unknown";
    }

    /// <summary>
    /// Map the strongest set intent bit to one of the existing contexts. Returns null when no
    /// intent bit is set or the bits are ambiguous (no clear winner among non-Family bits).
    /// Family / Schoolwork have no analogue and are ignored.
    /// </summary>
    private static string? MapUserIntentToContext(UserIntent intent, bool suppressGaming = false)
    {
        if (intent.Gaming && !suppressGaming) return "Gaming";
        if (intent.Entertainment) return "Plex";
        // Business / Development / Creativity / DevMode all bias toward Work
        if (intent.Business || intent.Development || intent.Creativity || intent.DevModeEnabled)
            return "Work";
        return null;
    }

    /// <summary>
    /// Read HKCU\…\CloudExperienceHost\Intent and HKLM\…\AppModelUnlock\devModeEnabled.
    /// Returns <see cref="UserIntent.None"/> on any failure — registry quirks must never
    /// prevent the service from constructing (it's a DI singleton).
    /// </summary>
    private static UserIntent ReadUserIntent()
    {
        try
        {
            int intent = 0;
            using (var k = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CloudExperienceHost\Intent"))
            {
                if (k?.GetValue("Intent") is int i) intent = i;
            }

            bool devMode = false;
            using (var k = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"))
            {
                if (k?.GetValue("devModeEnabled") is int d) devMode = d != 0;
            }

            return new UserIntent(
                Gaming:        (intent & 0b0000_0010) != 0,
                Family:        (intent & 0b0000_0100) != 0,
                Creativity:    (intent & 0b0000_1000) != 0,
                Schoolwork:    (intent & 0b0001_0000) != 0,
                Entertainment: (intent & 0b0010_0000) != 0,
                Business:      (intent & 0b0100_0000) != 0,
                Development:   (intent & 0b1000_0000) != 0,
                DevModeEnabled: devMode);
        }
        catch
        {
            return UserIntent.None;
        }
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
