using System;
using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace Optimizer.WinUI.Helpers;

/// <summary>
/// Best-effort shell + clipboard actions for list-row context menus (Batch 3, UI affordance).
/// Lives in Helpers rather than a ViewModel because it deliberately touches UI/shell types.
/// Every method swallows its own failures — a context-menu action must never crash the app.
/// </summary>
public static class RowActions
{
    /// <summary>Copy plain text to the clipboard.</summary>
    public static void CopyText(string? text)
    {
        try
        {
            var dp = new DataPackage();
            dp.SetText(text ?? string.Empty);
            Clipboard.SetContent(dp);
        }
        catch { /* clipboard contention is non-fatal */ }
    }

    /// <summary>Open Explorer with the file selected; falls back to opening its folder.</summary>
    public static void RevealInExplorer(string? fullPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return;
            if (File.Exists(fullPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{fullPath}\"") { UseShellExecute = true });
                return;
            }
            var dir = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Shell-open a target (an .msc snap-in, a file, a folder, or a URL).</summary>
    public static void ShellOpen(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>Open the user's browser on a web search for the given terms.</summary>
    public static void SearchOnline(string query)
    {
        try
        {
            var url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(query);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>Reveal a running process's executable in Explorer. Returns false when the path
    /// can't be resolved (access denied for a protected process, or it already exited).</summary>
    public static bool RevealProcessLocation(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var path = p.MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) return false;
            RevealInExplorer(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Terminate a process by PID. Returns (success, message) for user feedback.</summary>
    public static (bool ok, string message) TryEndProcess(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            var name = p.ProcessName;
            p.Kill();
            return (true, $"Afsluttede {name} (PID {pid}).");
        }
        catch (Exception ex)
        {
            return (false, $"Kunne ikke afslutte PID {pid}: {ex.Message}");
        }
    }

    /// <summary>Extract the executable path from a startup command line (handles quotes + args).</summary>
    public static string? ExtractExecutablePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command.Substring(1, end - 1) : command.Trim('"');
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command.Substring(0, space) : command;
    }
}
