namespace WindowsOptimizer.Models;

public class RegistrySetting
{
    public string HkeyBase { get; set; } = string.Empty;
    public string SubKey { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string ValueKind { get; set; } = "REG_SZ";
    public string ValueData { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresElevation { get; set; }
    public bool IsCritical { get; set; }
}
