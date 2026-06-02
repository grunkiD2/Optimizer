using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public enum RuleTrigger
{
    TimeRange,
    ProcessRunning
}

public partial class ProfileRule : ObservableObject
{
    [ObservableProperty] private string id = Guid.NewGuid().ToString();
    [ObservableProperty] private string name = "";
    [ObservableProperty] private RuleTrigger trigger;
    [ObservableProperty] private string profileId = "";
    [ObservableProperty] private string profileName = "";
    [ObservableProperty] private bool isEnabled = true;

    // For TimeRange
    [ObservableProperty] private TimeSpan startTime;
    [ObservableProperty] private TimeSpan endTime;

    // For ProcessRunning
    [ObservableProperty] private string processName = "";
}
