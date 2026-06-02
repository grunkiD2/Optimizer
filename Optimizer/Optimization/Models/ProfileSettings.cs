namespace WindowsOptimizer.Models;

public class NlaSetting
{
    public string SettingName { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class PowerSetting
{
    public string SettingName { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string PowerScheme { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class DisplaySetting
{
    public string SettingName { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string RefreshRate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
