using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.Mobile.Services;

namespace Optimizer.Mobile.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty] private string _serverUrl   = "—";
    [ObservableProperty] private string _statusText  = "—";
    [ObservableProperty] private bool _isConnected;

    public SettingsViewModel(ApiClient api)
    {
        _api = api;
    }

    public void Refresh()
    {
        ServerUrl  = _api.IsConfigured ? _api.ServerUrl : "—";
        StatusText = _api.IsConfigured ? "Connected" : "Disconnected";
        IsConnected = _api.IsConfigured;
    }

    [RelayCommand]
    public async Task DisconnectAsync()
    {
        bool? confirm = await Shell.Current.DisplayAlertAsync(
            "Disconnect",
            "Remove saved server connection?",
            "Disconnect", "Cancel");
        if (confirm != true) return;

        _api.Clear();
        Refresh();
        await Shell.Current.GoToAsync($"//{nameof(Pages.SetupPage)}");
    }

    [RelayCommand]
    public async Task TestConnectionAsync()
    {
        if (!_api.IsConfigured)
        {
            await Shell.Current.DisplayAlertAsync("Not configured", "No server configured.", "OK");
            return;
        }
        bool ok = await _api.TestConnectionAsync();
        await Shell.Current.DisplayAlertAsync(
            ok ? "Connected" : "Failed",
            ok ? $"Successfully reached {_api.ServerUrl}" : "Could not reach the server. Check network and token.",
            "OK");
        StatusText = ok ? "Connected" : "Unreachable";
    }
}
