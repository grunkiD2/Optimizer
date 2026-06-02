using System.Net.NetworkInformation;
using System.Text.Json;
using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public class FleetService : IFleetService
{
    private readonly string _rosterFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Optimizer", "fleet-roster.json");

    private List<FleetMachine> _machines = [];

    public FleetService() { Load(); }

    // ── Persistence ───────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (File.Exists(_rosterFile))
                _machines = JsonSerializer.Deserialize<List<FleetMachine>>(
                    File.ReadAllText(_rosterFile)) ?? [];
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rosterFile)!);
            File.WriteAllText(_rosterFile,
                JsonSerializer.Serialize(_machines,
                    new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ── IFleetService ─────────────────────────────────────────────────────────

    public Task<IReadOnlyList<FleetMachine>> GetRosterAsync()
        => Task.FromResult<IReadOnlyList<FleetMachine>>(_machines);

    public async Task<int> ImportFromCsvAsync(string csvPath)
    {
        if (!File.Exists(csvPath)) return 0;
        var lines = await File.ReadAllLinesAsync(csvPath);
        if (lines.Length < 2) return 0;

        // Expected header: Name,HostName,Department,Owner
        var imported = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;
            var machine = new FleetMachine
            {
                Name       = parts[0].Trim(),
                HostName   = parts[1].Trim(),
                Department = parts.Length > 2 ? parts[2].Trim() : "",
                Owner      = parts.Length > 3 ? parts[3].Trim() : ""
            };
            if (!_machines.Any(m => m.HostName.Equals(machine.HostName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _machines.Add(machine);
                imported++;
            }
        }
        Save();
        return imported;
    }

    public Task AddMachineAsync(FleetMachine m)
    {
        _machines.Add(m);
        Save();
        return Task.CompletedTask;
    }

    public Task DeleteMachineAsync(string name)
    {
        _machines.RemoveAll(m => m.Name == name);
        Save();
        return Task.CompletedTask;
    }

    public async Task<string> ExportToCsvAsync()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Optimizer Reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"fleet-roster-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        var lines = new List<string>
            { "Name,HostName,Department,Owner,Status,IPAddress,LastSeen" };
        lines.AddRange(_machines.Select(m =>
            $"{m.Name},{m.HostName},{m.Department},{m.Owner},{m.Status},{m.IpAddress},{m.LastSeen}"));

        await File.WriteAllLinesAsync(path, lines);
        return path;
    }

    public async Task<bool> PingMachineAsync(string hostName)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostName, 1500);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }
}
