using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Views;

namespace Optimizer.WinUI.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IWindowsOptimizerService _optimizer;

    [ObservableProperty] private int currentStep = 0;
    [ObservableProperty] private string selectedUsage = "Mixed";
    [ObservableProperty] private string selectedPrivacy = "Balanced";

    public List<string> UsageOptions   { get; } = ["Gaming", "Work / Productivity", "Mixed", "Content Creation"];
    public List<string> PrivacyOptions { get; } = ["Maximum Privacy", "Balanced", "Default"];

    public OnboardingViewModel(ISettingsService settings, IWindowsOptimizerService optimizer)
    {
        _settings  = settings;
        _optimizer = optimizer;
    }

    [RelayCommand]
    public void Next()
    {
        if (CurrentStep < 3) CurrentStep++;
    }

    [RelayCommand]
    public void Back()
    {
        if (CurrentStep > 0) CurrentStep--;
    }

    [RelayCommand]
    public async Task FinishAsync()
    {
        _settings.Settings.UsageProfile            = SelectedUsage;
        _settings.Settings.HasCompletedOnboarding  = true;
        _settings.Save();

        // Apply usage-based preset
        string? presetId = SelectedUsage switch
        {
            "Gaming"             => "preset-gaming",
            "Work / Productivity"=> "preset-productivity",
            "Content Creation"   => "preset-video-editing",
            _                    => null
        };
        if (presetId != null)
        {
            try { await _optimizer.ApplyProfileAsync(presetId); } catch { /* non-fatal */ }
        }

        // Apply privacy preset when Maximum Privacy is selected
        if (SelectedPrivacy == "Maximum Privacy")
        {
            try { await _optimizer.ApplyProfileAsync("preset-privacy"); } catch { /* non-fatal */ }
        }

        NavigateToHome();
    }

    [RelayCommand]
    public void Skip()
    {
        _settings.Settings.HasCompletedOnboarding = true;
        _settings.Save();
        NavigateToHome();
    }

    private static void NavigateToHome()
    {
        // Dashboard was retired in the IA redesign — Command Center is the home.
        var nav = App.GetService<NavigationService>();
        nav.NavigateTo(typeof(CommandCenterPage));
    }
}
