namespace Optimizer.WinUI.Services;

public class PowerPlan
{
    public Guid Guid { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public interface IPowerService
{
    Task<IReadOnlyList<PowerPlan>> GetPowerPlansAsync();
    Task<bool> SetActivePowerPlanAsync(Guid guid);
    Task<bool> CreateUltimatePerformancePlanAsync(); // duplicates the hidden ultimate performance plan
    bool IsGameModeEnabled();
    Task<bool> SetGameModeAsync(bool enabled);
}
