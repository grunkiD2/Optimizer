using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

public partial class UpdatesViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;

    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";
    [ObservableProperty] private string biosInfo = "";
    [ObservableProperty] private bool isUpgradingAll;

    public ObservableCollection<WindowsUpdateInfo> RecentUpdates { get; } = [];
    public ObservableCollection<AppUpdateInfo> AppUpdates { get; } = [];

    public string CategoryName => "Updates";
    public string CategoryIcon => "\U0001F504";  // 🔄

    public bool HasAppUpdates   => AppUpdates.Count > 0;
    public bool HasNoAppUpdates => AppUpdates.Count == 0;
    public bool HasNoRecentUpdates => RecentUpdates.Count == 0;

    public UpdatesViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Loading update information…";
        try
        {
            // Load all three sections in parallel
            var wuTask    = _updateService.GetRecentWindowsUpdatesAsync(60);
            var wingetTask = _updateService.GetWingetUpdatesAsync();
            var biosTask  = _updateService.GetBiosInfoAsync();

            await Task.WhenAll(wuTask, wingetTask, biosTask);

            RecentUpdates.Clear();
            foreach (var u in wuTask.Result)
                RecentUpdates.Add(u);

            AppUpdates.Clear();
            foreach (var a in wingetTask.Result)
                AppUpdates.Add(a);

            BiosInfo = biosTask.Result;

            OnPropertyChanged(nameof(HasAppUpdates));
            OnPropertyChanged(nameof(HasNoAppUpdates));
            OnPropertyChanged(nameof(HasNoRecentUpdates));
            StatusMessage = BuildSummary();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load updates: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CheckForWindowsUpdatesAsync()
    {
        StatusMessage = "Opening Windows Update…";
        var ok = await _updateService.RunWindowsUpdateCheckAsync();
        if (!ok) StatusMessage = "Could not open Windows Update settings.";
    }

    [RelayCommand]
    public async Task UpgradeAppAsync(AppUpdateInfo app)
    {
        StatusMessage = $"Upgrading {app.Name}…";
        IsLoading = true;
        try
        {
            var ok = await _updateService.UpgradeAppAsync(app.Id);
            StatusMessage = ok
                ? $"{app.Name} upgraded successfully."
                : $"Could not upgrade {app.Name}. Try running as administrator.";
            if (ok)
            {
                AppUpdates.Remove(app);
                OnPropertyChanged(nameof(HasAppUpdates));
                OnPropertyChanged(nameof(HasNoAppUpdates));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error upgrading {app.Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task UpgradeAllAsync()
    {
        if (AppUpdates.Count == 0) return;
        IsUpgradingAll = true;
        IsLoading      = true;
        StatusMessage  = "Upgrading all applications… (this may take several minutes)";
        try
        {
            var ok = await _updateService.UpgradeAllAppsAsync();
            StatusMessage = ok
                ? "All applications upgraded successfully."
                : "Some upgrades may have failed. Check winget output for details.";
            if (ok)
            {
                AppUpdates.Clear();
                OnPropertyChanged(nameof(HasAppUpdates));
                OnPropertyChanged(nameof(HasNoAppUpdates));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during upgrade-all: {ex.Message}";
        }
        finally
        {
            IsLoading      = false;
            IsUpgradingAll = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string BuildSummary()
    {
        var parts = new List<string>();
        if (RecentUpdates.Count > 0)
            parts.Add($"{RecentUpdates.Count} Windows update(s) in the last 60 days");
        if (AppUpdates.Count > 0)
            parts.Add($"{AppUpdates.Count} app update(s) available");
        else
            parts.Add("All apps are up to date");
        return string.Join(" · ", parts);
    }
}
