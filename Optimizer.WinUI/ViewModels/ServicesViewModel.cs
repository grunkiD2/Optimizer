using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class ServicesViewModel : ObservableObject
{
    private readonly IServiceManagerService _serviceManager;
    private List<WindowsServiceInfo> _allServices = [];

    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string filterStatus = "All";
    [ObservableProperty] private string filterRecommendation = "All";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private int totalCount;
    [ObservableProperty] private int runningCount;

    public ObservableCollection<WindowsServiceInfo> Services { get; } = [];

    public List<string> StatusOptions { get; } = ["All", "Running", "Stopped"];
    public List<string> RecommendationOptions { get; } = ["All", "Safe", "Caution", "Critical", "Unknown"];

    public string CategoryName => "Services";
    public string CategoryIcon => "⚙️";

    public ServicesViewModel(IServiceManagerService serviceManager)
    {
        _serviceManager = serviceManager;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            _allServices = (await _serviceManager.GetServicesAsync()).ToList();
            TotalCount   = _allServices.Count;
            RunningCount = _allServices.Count(s => s.Status == "Running");
            ApplyFilters();
        }
        finally { IsLoading = false; }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnFilterStatusChanged(string value) => ApplyFilters();
    partial void OnFilterRecommendationChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        IEnumerable<WindowsServiceInfo> filtered = _allServices;

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(s =>
                s.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                s.ServiceName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        if (FilterStatus != "All")
            filtered = filtered.Where(s => s.Status == FilterStatus);

        if (FilterRecommendation != "All")
            filtered = filtered.Where(s => s.Recommendation == FilterRecommendation);

        Services.Clear();
        foreach (var s in filtered.Take(500)) Services.Add(s); // cap to prevent UI bloat
    }

    public async Task ToggleServiceAsync(WindowsServiceInfo svc)
    {
        if (svc.Status == "Running")
            await _serviceManager.StopServiceAsync(svc.ServiceName);
        else
            await _serviceManager.StartServiceAsync(svc.ServiceName);
        await LoadAsync();
    }

    public async Task SetStartupTypeAsync(WindowsServiceInfo svc, string startupType)
    {
        await _serviceManager.SetStartupTypeAsync(svc.ServiceName, startupType);
        await LoadAsync();
    }
}
