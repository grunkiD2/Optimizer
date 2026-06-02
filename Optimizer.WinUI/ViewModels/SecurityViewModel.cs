using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.ViewModels;

/// <summary>A simple recommendation item displayed in the Security page.</summary>
public class SecurityRecommendation
{
    public string Icon { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string SeverityColor { get; set; } = "#F59E0B"; // amber default
}

public partial class SecurityViewModel : ObservableObject
{
    private readonly ISecurityService _securityService;

    // ── Loading / status ──────────────────────────────────────────────────────
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string statusMessage = "";

    // ── Security score ────────────────────────────────────────────────────────
    [ObservableProperty] private int securityScore;
    [ObservableProperty] private string scoreLabel = "—";
    [ObservableProperty] private string scoreForeground = "#6B7280";

    // ── Defender ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool realTimeProtectionEnabled;
    [ObservableProperty] private bool cloudProtectionEnabled;
    [ObservableProperty] private bool tamperProtectionEnabled;
    [ObservableProperty] private string lastQuickScan = "Never";
    [ObservableProperty] private string lastFullScan  = "Never";
    [ObservableProperty] private string definitionVersion = "";

    // ── Firewall ──────────────────────────────────────────────────────────────
    [ObservableProperty] private bool domainFirewallEnabled;
    [ObservableProperty] private bool privateFirewallEnabled;
    [ObservableProperty] private bool publicFirewallEnabled;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<BitLockerVolume> BitLockerVolumes { get; } = [];
    public ObservableCollection<SecurityRecommendation> Recommendations { get; } = [];

    public string CategoryName => "Security";
    public string CategoryIcon => "🛡️"; // 🛡️

    public bool HasBitLockerVolumes    => BitLockerVolumes.Count > 0;
    public bool HasNoBitLockerVolumes  => BitLockerVolumes.Count == 0;
    public bool HasRecommendations    => Recommendations.Count > 0;
    public bool HasNoRecommendations  => Recommendations.Count == 0;

    public SecurityViewModel(ISecurityService securityService)
    {
        _securityService = securityService;
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusMessage = "Checking security status…";
        try
        {
            var defTask = _securityService.GetDefenderStatusAsync();
            var fwTask  = _securityService.GetFirewallStatusAsync();
            var blTask  = _securityService.GetBitLockerStatusAsync();
            var scoreTask = _securityService.GetSecurityScoreAsync();

            await Task.WhenAll(defTask, fwTask, blTask, scoreTask);

            ApplyDefender(defTask.Result);
            ApplyFirewall(fwTask.Result);
            ApplyBitLocker(blTask.Result);
            ApplyScore(scoreTask.Result);
            BuildRecommendations(defTask.Result, fwTask.Result, blTask.Result);

            StatusMessage = $"Security score: {SecurityScore}/100";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load security data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RunQuickScanAsync()
    {
        IsLoading = true;
        StatusMessage = "Starting Quick Scan… (running in background)";
        try
        {
            var ok = await _securityService.RunQuickScanAsync();
            StatusMessage = ok
                ? "Quick Scan started successfully."
                : "Could not start Quick Scan. Ensure Windows Defender is enabled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error starting Quick Scan: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void ApplyDefender(DefenderStatus d)
    {
        RealTimeProtectionEnabled = d.RealTimeProtectionEnabled;
        CloudProtectionEnabled    = d.CloudProtectionEnabled;
        TamperProtectionEnabled   = d.TamperProtectionEnabled;
        DefinitionVersion         = d.DefinitionVersion;
        LastQuickScan = FormatScanTime(d.LastQuickScan);
        LastFullScan  = FormatScanTime(d.LastFullScan);
    }

    private void ApplyFirewall(FirewallStatus fw)
    {
        DomainFirewallEnabled  = fw.DomainEnabled;
        PrivateFirewallEnabled = fw.PrivateEnabled;
        PublicFirewallEnabled  = fw.PublicEnabled;
    }

    private void ApplyBitLocker(IReadOnlyList<BitLockerVolume> volumes)
    {
        BitLockerVolumes.Clear();
        foreach (var v in volumes)
            BitLockerVolumes.Add(v);
        OnPropertyChanged(nameof(HasBitLockerVolumes));
        OnPropertyChanged(nameof(HasNoBitLockerVolumes));
    }

    private void ApplyScore(int score)
    {
        SecurityScore = score;
        (ScoreLabel, ScoreForeground) = score switch
        {
            >= 90 => ("Excellent", "#10B981"),
            >= 70 => ("Good",      "#3B82F6"),
            >= 50 => ("Fair",      "#F59E0B"),
            _     => ("Weak",      "#EF4444")
        };
    }

    private void BuildRecommendations(DefenderStatus def, FirewallStatus fw, IReadOnlyList<BitLockerVolume> bl)
    {
        Recommendations.Clear();

        if (!def.RealTimeProtectionEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "⚠️",
                Title = "Enable Real-Time Protection",
                Detail = "Windows Defender real-time protection is off. Your system is exposed to threats.",
                SeverityColor = "#EF4444"
            });

        if (!def.CloudProtectionEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "☁️",
                Title = "Enable Cloud-Delivered Protection",
                Detail = "Cloud protection provides faster detection of new malware.",
                SeverityColor = "#F59E0B"
            });

        if (!def.TamperProtectionEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "🔒",
                Title = "Enable Tamper Protection",
                Detail = "Tamper Protection prevents malware from disabling Defender.",
                SeverityColor = "#F59E0B"
            });

        if (!fw.DomainEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "🔥",
                Title = "Enable Domain Firewall Profile",
                Detail = "The Domain network firewall profile is disabled.",
                SeverityColor = "#F59E0B"
            });

        if (!fw.PrivateEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "🔥",
                Title = "Enable Private Firewall Profile",
                Detail = "The Private network firewall profile is disabled.",
                SeverityColor = "#EF4444"
            });

        if (!fw.PublicEnabled)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "🔥",
                Title = "Enable Public Firewall Profile",
                Detail = "The Public network firewall profile is disabled — this is high risk on untrusted networks.",
                SeverityColor = "#EF4444"
            });

        // Check system drive BitLocker
        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System))?.TrimEnd('\\') ?? "C:";
        var sysBl = bl.FirstOrDefault(v =>
            v.DriveLetter.TrimEnd('\\').Equals(systemDrive, StringComparison.OrdinalIgnoreCase));

        bool sysBlOn = sysBl != null &&
            (sysBl.ProtectionStatus.Contains("On", StringComparison.OrdinalIgnoreCase)
             || sysBl.ProtectionStatus == "1");

        if (!sysBlOn)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "💾",
                Title = "Enable BitLocker on System Drive",
                Detail = $"Drive {systemDrive} is not BitLocker-encrypted. Encryption protects data if the device is lost or stolen.",
                SeverityColor = "#F59E0B"
            });

        if (Recommendations.Count == 0)
            Recommendations.Add(new SecurityRecommendation
            {
                Icon  = "✅",
                Title = "Your system security looks good!",
                Detail = "No critical issues found. Keep your definitions up to date.",
                SeverityColor = "#10B981"
            });

        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(HasNoRecommendations));
    }

    private static string FormatScanTime(DateTime dt)
    {
        if (dt == default || dt.Year < 2000) return "Never";
        var local = dt.ToLocalTime();
        var age   = DateTime.Now - local;
        if (age.TotalDays < 1) return $"Today at {local:h:mm tt}";
        if (age.TotalDays < 2) return $"Yesterday at {local:h:mm tt}";
        return local.ToString("MMM d, yyyy");
    }
}
