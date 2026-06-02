using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Optimizer.WinUI.Services;
using Optimizer.WinUI.Services.Cloud;
using Optimizer.WinUI.Services.Plugins;

namespace Optimizer.WinUI.ViewModels;

// ── Item view-models ──────────────────────────────────────────────────────────

public partial class InstalledPluginVm : ObservableObject
{
    public string PluginId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Author { get; init; } = "";
    public string Category { get; init; } = "";
    public string Description { get; init; } = "";
    public string FilePath { get; init; } = "";
    public IReadOnlyList<string> PermissionViolations { get; init; } = [];

    [ObservableProperty] private bool isEnabled;

    public bool HasViolations => PermissionViolations.Count > 0;

    public static InstalledPluginVm From(LoadedPlugin p) => new()
    {
        PluginId           = p.Manifest.Id,
        Name               = p.Manifest.Name,
        Author             = p.Manifest.Author,
        Category           = p.Manifest.Category,
        Description        = p.Manifest.Description,
        FilePath           = p.FilePath,
        PermissionViolations = p.PermissionViolations,
        IsEnabled          = p.Enabled
    };
}

public class RemotePluginVm
{
    public string PluginId { get; init; } = "";
    public string Name { get; init; } = "";
    public string AuthorDisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public int Downloads { get; init; }
    public double AverageRating { get; init; }
    public int RatingCount { get; init; }
    public bool Verified { get; init; }

    public string DownloadsText => Downloads >= 1000
        ? $"{(Downloads / 1000.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}K"
        : Downloads.ToString();

    public string RatingText => RatingCount > 0
        ? $"{AverageRating:F1} ({RatingCount})"
        : "No ratings";

    public bool IsAlreadyInstalled { get; set; }

    public static RemotePluginVm From(RemotePluginListing l) => new()
    {
        PluginId          = l.PluginId,
        Name              = l.Name,
        AuthorDisplayName = l.AuthorDisplayName,
        Description       = l.Description,
        Category          = l.Category,
        Downloads         = l.Downloads,
        AverageRating     = l.AverageRating,
        RatingCount       = l.RatingCount,
        Verified          = l.Verified
    };
}

// ── Main ViewModel ────────────────────────────────────────────────────────────

public partial class PluginsViewModel : ObservableObject
{
    private readonly IPluginLoader _loader;
    private readonly IOptimizerCloudClient _cloud;
    private readonly IPluginVerifier _verifier;
    private readonly IManifestParser _parser;
    private readonly WindowsOptimizerService _optimizer;

    [ObservableProperty] private bool isLoadingInstalled;
    [ObservableProperty] private bool isLoadingAvailable;
    [ObservableProperty] private string statusMessage = "";

    public ObservableCollection<InstalledPluginVm> Installed { get; } = [];
    public ObservableCollection<RemotePluginVm> Available { get; } = [];

    public bool CanSubmit => _cloud.IsAuthenticated;
    public bool HasServerUrl => _cloud.ServerUrl != null;

    public bool IsInstalledEmpty => !IsLoadingInstalled && Installed.Count == 0;
    public bool IsAvailableEmpty => !IsLoadingAvailable && Available.Count == 0 && HasServerUrl;
    public bool ShowNoServer => !HasServerUrl;

    public PluginsViewModel(
        IPluginLoader loader,
        IOptimizerCloudClient cloud,
        IPluginVerifier verifier,
        IManifestParser parser,
        IWindowsOptimizerService optimizer)
    {
        _loader    = loader;
        _cloud     = cloud;
        _verifier  = verifier;
        _parser    = parser;
        _optimizer = (WindowsOptimizerService)optimizer;

        Installed.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsInstalledEmpty));
        };
        Available.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsAvailableEmpty));
        };
    }

    partial void OnIsLoadingInstalledChanged(bool value) => OnPropertyChanged(nameof(IsInstalledEmpty));
    partial void OnIsLoadingAvailableChanged(bool value) => OnPropertyChanged(nameof(IsAvailableEmpty));

    // ── Installed section ─────────────────────────────────────────────────────

    [RelayCommand]
    public Task RefreshInstalledAsync()
    {
        IsLoadingInstalled = true;
        try
        {
            _loader.Reload();
            var installedIds = _loader.LoadedPlugins.Select(p => p.Manifest.Id).ToHashSet();

            Installed.Clear();
            foreach (var p in _loader.LoadedPlugins)
                Installed.Add(InstalledPluginVm.From(p));

            // Mark installed ones in Available
            foreach (var vm in Available)
                vm.IsAlreadyInstalled = installedIds.Contains(vm.PluginId);
        }
        finally
        {
            IsLoadingInstalled = false;
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task ToggleInstalledAsync(InstalledPluginVm vm)
    {
        if (vm.HasViolations) return Task.CompletedTask;
        var newState = !vm.IsEnabled;
        _loader.SetEnabled(vm.PluginId, newState);
        vm.IsEnabled = newState;
        _optimizer.RefreshHandlers();
        return Task.CompletedTask;
    }

    [RelayCommand]
    public Task RemoveInstalledAsync(InstalledPluginVm vm)
    {
        _loader.Remove(vm.PluginId);
        Installed.Remove(vm);
        _optimizer.RefreshHandlers();

        // Un-mark in Available
        var avail = Available.FirstOrDefault(a => a.PluginId == vm.PluginId);
        if (avail != null) avail.IsAlreadyInstalled = false;

        return Task.CompletedTask;
    }

    // ── Available section ─────────────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadAvailableAsync()
    {
        if (_cloud.ServerUrl == null) return;
        IsLoadingAvailable = true;
        try
        {
            var result = await _cloud.BrowsePluginsAsync(null, null, "downloads", 1, 50);
            if (result == null) return;

            var installedIds = _loader.LoadedPlugins.Select(p => p.Manifest.Id).ToHashSet();

            Available.Clear();
            foreach (var l in result.Listings)
            {
                var vm = RemotePluginVm.From(l);
                vm.IsAlreadyInstalled = installedIds.Contains(vm.PluginId);
                Available.Add(vm);
            }
        }
        finally
        {
            IsLoadingAvailable = false;
        }
    }

    /// <summary>
    /// Fetch detail + verify + write to temp + install + refresh.
    /// Returns a <see cref="PluginInstallOutcome"/> describing what happened so the UI can show dialogs.
    /// </summary>
    public async Task<PluginInstallOutcome> PrepareInstallAsync(string pluginId)
    {
        var detail = await _cloud.GetPluginDetailAsync(pluginId);
        if (detail == null)
            return new PluginInstallOutcome(false, null, null, "Could not fetch plugin from server.", null);

        var verification = _verifier.Verify(detail.ManifestYaml, detail.Signature);

        // Parse to extract change list for the warning dialog
        var parseResult = _parser.ParseYaml(detail.ManifestYaml);
        var changes     = parseResult.Manifest?.Changes.Select(c => $"{c.Type}: {c.Path}").ToList()
                          ?? new List<string>();

        return new PluginInstallOutcome(
            Success: true,
            Detail: detail,
            VerificationResult: verification,
            ErrorMessage: null,
            Changes: changes);
    }

    public async Task<bool> CompleteInstallAsync(RemotePluginDetail detail)
    {
        // Write to temp file and call PluginLoader.InstallFromFileAsync
        var tempPath = Path.Combine(Path.GetTempPath(), $"{detail.PluginId}_{Guid.NewGuid():N}.yaml");
        try
        {
            await File.WriteAllTextAsync(tempPath, detail.ManifestYaml);
            var ok = await _loader.InstallFromFileAsync(tempPath);
            if (!ok) return false;

            _optimizer.RefreshHandlers();

            // Increment download counter (fire-and-forget)
            _ = _cloud.IncrementPluginDownloadAsync(detail.PluginId);

            // Refresh both lists
            await RefreshInstalledAsync();

            // Mark as installed in Available
            var avail = Available.FirstOrDefault(a => a.PluginId == detail.PluginId);
            if (avail != null) avail.IsAlreadyInstalled = true;

            return true;
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
        }
    }

    // ── Submit section ────────────────────────────────────────────────────────

    public async Task<SubmitPluginOutcome> SubmitPluginAsync(string yamlContent)
    {
        var parseResult = _parser.ParseYaml(yamlContent);
        if (!parseResult.Success)
            return new SubmitPluginOutcome(false, string.Join("; ", parseResult.Errors));

        var ok = await _cloud.SubmitPluginAsync(yamlContent);
        return ok
            ? new SubmitPluginOutcome(true, null)
            : new SubmitPluginOutcome(false, "Server rejected the submission. Check the manifest is valid.");
    }
}

// ── Outcome types ─────────────────────────────────────────────────────────────

public record PluginInstallOutcome(
    bool Success,
    RemotePluginDetail? Detail,
    VerificationResult? VerificationResult,
    string? ErrorMessage,
    IReadOnlyList<string>? Changes = null);

public record SubmitPluginOutcome(bool Success, string? ErrorMessage);
