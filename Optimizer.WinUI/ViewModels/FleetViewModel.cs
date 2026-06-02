using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class FleetViewModel : ObservableObject
{
    private readonly IFleetService _fleet;
    private List<FleetMachine> _allMachines = [];

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string searchText = "";

    // Add-machine dialog fields
    [ObservableProperty] private string newName       = "";
    [ObservableProperty] private string newHostName   = "";
    [ObservableProperty] private string newDepartment = "";
    [ObservableProperty] private string newOwner      = "";

    public ObservableCollection<FleetMachine> Machines { get; } = [];

    public string CategoryName => "Fleet";
    public string CategoryIcon => "🖧";

    public FleetViewModel(IFleetService fleet)
    {
        _fleet = fleet;
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        try
        {
            _allMachines = (await _fleet.GetRosterAsync()).ToList();
            ApplyFilter();
            StatusMessage = _allMachines.Count == 0
                ? "No machines in roster. Import a CSV or add machines manually."
                : $"{_allMachines.Count} machine(s) in roster.";
        }
        finally { IsLoading = false; }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<FleetMachine> filtered = _allMachines;
        if (!string.IsNullOrWhiteSpace(SearchText))
            filtered = filtered.Where(m =>
                m.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.HostName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                m.Department.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        Machines.Clear();
        foreach (var m in filtered) Machines.Add(m);
    }

    // ── Import CSV ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ImportCsvAsync(string csvPath)
    {
        IsLoading = true;
        try
        {
            var count = await _fleet.ImportFromCsvAsync(csvPath);
            await LoadAsync();
            StatusMessage = $"Imported {count} machine(s) from CSV.";
        }
        finally { IsLoading = false; }
    }

    // ── Add machine ───────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task AddMachineAsync()
    {
        if (string.IsNullOrWhiteSpace(NewHostName)) return;
        var machine = new FleetMachine
        {
            Name       = string.IsNullOrWhiteSpace(NewName) ? NewHostName : NewName,
            HostName   = NewHostName,
            Department = NewDepartment,
            Owner      = NewOwner
        };
        await _fleet.AddMachineAsync(machine);
        NewName = NewHostName = NewDepartment = NewOwner = "";
        await LoadAsync();
    }

    // ── Delete machine ────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task DeleteMachineAsync(string name)
    {
        await _fleet.DeleteMachineAsync(name);
        await LoadAsync();
    }

    // ── Ping machine ──────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task PingMachineAsync(FleetMachine machine)
    {
        machine.Status = "Unknown";
        var alive = await _fleet.PingMachineAsync(machine.HostName);
        machine.Status   = alive ? "Online" : "Offline";
        machine.LastSeen = alive ? DateTime.Now.ToString("yyyy-MM-dd HH:mm") : machine.LastSeen;
        // Trigger property notification so UI refreshes the status color
        OnPropertyChanged(nameof(Machines));
    }

    // ── Ping all ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task PingAllAsync()
    {
        IsLoading = true;
        try
        {
            var tasks = _allMachines.Select(m => PingMachineAsync(m));
            await Task.WhenAll(tasks);
            StatusMessage = $"Ping complete. {_allMachines.Count(m => m.Status == "Online")} online, " +
                            $"{_allMachines.Count(m => m.Status == "Offline")} offline.";
        }
        finally { IsLoading = false; }
    }

    // ── Export CSV ────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        var path = await _fleet.ExportToCsvAsync();
        StatusMessage = $"Exported to: {path}";
    }
}
