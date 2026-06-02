namespace Optimizer.WinUI.Models;

public class SettingsProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ProfileType ProfileType { get; set; } = ProfileType.Custom;
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAppliedAt { get; set; }

    public List<RegistrySetting> RegistrySettings { get; set; } = new();

    /// <summary>Optimization IDs bundled into this profile; applied via the engine's optimization pipeline.</summary>
    public List<string> Optimizations { get; set; } = new();

    /// <summary>Captured startup on/off states restored when this profile is applied.</summary>
    public List<StartupState> StartupStates { get; set; } = new();
}

public enum ProfileType
{
    Gaming,
    Productivity,
    BatterySaver,
    Performance,
    Custom
}
