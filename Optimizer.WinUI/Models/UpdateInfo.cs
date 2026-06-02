namespace Optimizer.WinUI.Models;

public class WindowsUpdateInfo
{
    public string Title { get; set; } = "";
    public string KbNumber { get; set; } = "";
    public DateTime InstalledOn { get; set; }
    public string Status { get; set; } = ""; // Installed, Failed, Pending
}

public class AppUpdateInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Source { get; set; } = ""; // winget, store, manual
}
