using Optimizer.Mobile.Services;

namespace Optimizer.Mobile.Pages;

public partial class SetupPage : ContentPage
{
    private readonly ApiClient _api;

    public SetupPage(ApiClient api)
    {
        InitializeComponent();
        _api = api;

        // Pre-fill if previously configured
        if (_api.IsConfigured)
        {
            UrlEntry.Text   = _api.ServerUrl;
            TokenEntry.Text = _api.Token;
        }
    }

    private async void OnConnectClicked(object sender, EventArgs e)
    {
        var url   = UrlEntry.Text?.Trim().TrimEnd('/') ?? "";
        var token = TokenEntry.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token))
        {
            ShowError("Please fill in both fields.");
            return;
        }

        Spinner.IsRunning = true;
        Spinner.IsVisible = true;
        StatusLabel.IsVisible = false;

        bool ok = await ApiClient.ProbeAsync(url, token);

        Spinner.IsRunning = false;
        Spinner.IsVisible = false;

        if (!ok)
        {
            ShowError($"Could not connect to {url}. Check the URL, token, and network.");
            return;
        }

        _api.SaveConfig(url, token);
        await Shell.Current.GoToAsync("//dashboard", animate: true);
    }

    private void ShowError(string msg)
    {
        StatusLabel.Text = msg;
        StatusLabel.IsVisible = true;
    }
}
