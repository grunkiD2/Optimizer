using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IFleetService
{
    Task<IReadOnlyList<FleetMachine>> GetRosterAsync();
    Task<int> ImportFromCsvAsync(string csvPath);   // returns count imported
    Task AddMachineAsync(FleetMachine machine);
    Task DeleteMachineAsync(string name);
    Task<string> ExportToCsvAsync();                // returns path
    Task<bool> PingMachineAsync(string hostName);
}
