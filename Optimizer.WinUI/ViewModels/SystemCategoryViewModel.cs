using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Optimizer.WinUI.Services;
using Ids = Optimizer.WinUI.Models.OptimizationIds;

namespace Optimizer.WinUI.ViewModels;

public partial class SystemCategoryViewModel : CategoryViewModelBase
{
    private readonly IPrivacyService _privacyService;

    [ObservableProperty] private string telemetryStatus = "Unknown";
    [ObservableProperty] private int privacyScore;
    [ObservableProperty] private string privacyScoreText = "—";

    public ObservableCollection<PrivacySetting> PrivacySettings { get; } = [];

    public override string CategoryName => "System";
    public override string CategoryIcon => "🖥️";

    protected override string[] OptimizationIds =>
    [
        Ids.DisableTelemetry,
        Ids.DisableConsumerFeatures,
        Ids.DisableHibernation,
        // Audit Batch 2: previously orphaned (registered + implemented, but on no page).
        Ids.ConfigureWindowsUpdateUX,
        Ids.DisableAutoplay,
        Ids.DisableUsbNotifications
    ];

    public SystemCategoryViewModel(
        IWindowsOptimizerService optimizer,
        IElevationService elevation,
        IUndoService undoSvc,
        IHistoryService history,
        IPrivacyService privacyService)
        : base(optimizer, elevation, undoSvc, history)
    {
        _privacyService = privacyService;
    }

    public override void Load()
    {
        base.Load();
        RefreshMetrics();
    }

    public async Task LoadPrivacyAsync()
    {
        var all = await _privacyService.GetAllAsync();
        PrivacySettings.Clear();
        foreach (var s in all)
            PrivacySettings.Add(s);

        UpdatePrivacyScore();
    }

    /// <summary>Returns false on failure so the page can revert the toggle (audit Batch 2:
    /// a failed privacy change used to leave the switch showing a state that didn't apply).</summary>
    public async Task<bool> ToggleAsync(PrivacySetting setting, bool enableForPrivacy)
    {
        if (await _privacyService.SetEnabledAsync(setting.Id, enableForPrivacy))
        {
            setting.IsPrivacyFriendly = enableForPrivacy;
            UpdatePrivacyScore();
            SetStatus($"{setting.Name}: {(enableForPrivacy ? "privacy-friendly" : "default")}.", false);
            return true;
        }
        SetStatus($"Couldn't change \"{setting.Name}\" — requires administrator.", true);
        return false;
    }

    public void RefreshMetrics()
    {
        TelemetryStatus = Optimizer.IsOptimizationApplied(Ids.DisableTelemetry) == true
            ? "Disabled"
            : "Active";
    }

    private void UpdatePrivacyScore()
    {
        if (PrivacySettings.Count == 0)
        {
            PrivacyScore = 0;
            PrivacyScoreText = "—";
            return;
        }
        PrivacyScore = (int)(100.0 * PrivacySettings.Count(s => s.IsPrivacyFriendly) / PrivacySettings.Count);
        PrivacyScoreText = $"{PrivacyScore}/100";
    }
}
