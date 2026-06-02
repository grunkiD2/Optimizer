using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

using Microsoft.Win32;

using Optimizer.Helpers;
using Optimizer.Services;

using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace Optimizer.ViewModels
{
    public class ProfilesViewModel : Observable
    {
        private readonly IWindowsOptimizerService _optimizerService;
        private readonly IStartupService _startupService;
        private readonly ISchedulerService _scheduler;

        private SettingsProfile _selectedProfile;
        private SettingsProfile _selectedPreset;
        private string _newProfileName = string.Empty;
        private ProfileType _newProfileType = ProfileType.Custom;
        private bool _captureStartupState;
        private string _statusMessage = "No profile selected.";
        private bool _isBusy;
        private string _previewSummary = "Select a profile to see what applying it will change.";
        private bool _previewRequiresAdmin;
        private string? _editingId;

        public ProfilesViewModel(IWindowsOptimizerService optimizerService, IStartupService startupService, ISchedulerService scheduler)
        {
            _optimizerService = optimizerService;
            _startupService = startupService;
            _scheduler = scheduler;

            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            CreateCommand = new RelayCommand(async () => await CreateAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(NewProfileName));
            EditCommand = new RelayCommand(BeginEdit, () => !IsBusy && SelectedProfile != null);
            CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);
            ApplyCommand = new RelayCommand(async () => await ApplyAsync(), () => !IsBusy && SelectedProfile != null);
            DeleteCommand = new RelayCommand(async () => await DeleteAsync(), () => !IsBusy && SelectedProfile != null);
            RevertCommand = new RelayCommand(async () => await RevertAsync(), () => !IsBusy && SelectedProfile != null);
            AddPresetCommand = new RelayCommand(async () => await AddPresetAsync(), () => !IsBusy && SelectedPreset != null);
            ExportCommand = new RelayCommand(Export, () => SelectedProfile != null);
            ImportCommand = new RelayCommand(async () => await ImportAsync(), () => !IsBusy);
            ScheduleLogonCommand = new RelayCommand(ScheduleLogon, () => !IsBusy && SelectedProfile != null);
            RemoveScheduleCommand = new RelayCommand(RemoveSchedule, () => !IsBusy && SelectedProfile != null);

            LoadPresets();
            _ = LoadAsync();
            _ = LoadOptimizationChoicesAsync();
        }

        public ObservableCollection<SettingsProfile> Profiles { get; } = new();

        /// <summary>Optimizations the user can tick to bundle into a new profile.</summary>
        public ObservableCollection<OptimizationChoice> AvailableOptimizations { get; } = new();

        /// <summary>Built-in read-only preset profiles.</summary>
        public ObservableCollection<SettingsProfile> Presets { get; } = new();

        public Array ProfileTypes => Enum.GetValues(typeof(ProfileType));

        public SettingsProfile SelectedPreset
        {
            get => _selectedPreset;
            set { Set(ref _selectedPreset, value); (AddPresetCommand as RelayCommand)?.OnCanExecuteChanged(); }
        }

        public bool CaptureStartupState
        {
            get => _captureStartupState;
            set => Set(ref _captureStartupState, value);
        }

        public SettingsProfile SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                Set(ref _selectedProfile, value);
                BuildPreview();
                OnPropertyChanged(nameof(HasSelection));
                (ApplyCommand as RelayCommand)?.OnCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.OnCanExecuteChanged();
                (RevertCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }

        /// <summary>Human-readable list of the changes the selected profile will make on Apply.</summary>
        public ObservableCollection<string> PreviewChanges { get; } = new();

        public bool HasSelection => _selectedProfile != null;

        public bool PreviewHasChanges => PreviewChanges.Count > 0;

        public bool PreviewRequiresAdmin
        {
            get => _previewRequiresAdmin;
            set => Set(ref _previewRequiresAdmin, value);
        }

        public string PreviewSummary
        {
            get => _previewSummary;
            set => Set(ref _previewSummary, value);
        }

        private void BuildPreview()
        {
            PreviewChanges.Clear();
            var p = _selectedProfile;

            if (p == null)
            {
                PreviewRequiresAdmin = false;
                PreviewSummary = "Select a profile to see what applying it will change.";
                OnPropertyChanged(nameof(PreviewHasChanges));
                return;
            }

            foreach (var r in p.RegistrySettings)
            {
                var admin = r.RequiresElevation || r.HkeyBase.Contains("LOCAL_MACHINE") || r.HkeyBase == "HKLM";
                PreviewChanges.Add($"Registry: {r.HkeyBase}\\{r.SubKey}\\{r.ValueName} = {r.ValueData} ({r.ValueKind}){(admin ? "  [admin]" : "")}");
            }
            foreach (var pw in p.PowerSettings)
                PreviewChanges.Add($"Power: {pw.SettingName} = {pw.SettingValue}");
            foreach (var d in p.DisplaySettings)
                PreviewChanges.Add($"Display: {d.SettingName} = {d.SettingValue}");
            foreach (var n in p.NetworkSettings)
                PreviewChanges.Add($"Network: {n.SettingName} = {n.SettingValue}");
            foreach (var kv in p.Settings)
                PreviewChanges.Add($"Setting: {kv.Key} = {kv.Value}");

            var optimizationNeedsAdmin = false;
            foreach (var optId in p.Optimizations)
            {
                var info = _optimizerService.GetOptimizationInfo(optId);
                if (info == null)
                {
                    PreviewChanges.Add($"Optimization: {optId}");
                    continue;
                }

                var applied = _optimizerService.IsOptimizationApplied(optId);
                var appliedTag = applied == true ? "  ✓ already applied" : applied == false ? "  • not yet applied" : "";
                PreviewChanges.Add($"Optimization: {info.Title}{(info.RequiresAdmin ? "  [admin]" : "")}{appliedTag}");
                foreach (var change in info.Changes)
                {
                    PreviewChanges.Add($"      {change}");
                }
                optimizationNeedsAdmin |= info.RequiresAdmin;
            }

            if (p.StartupStates.Count > 0)
            {
                var enabled = p.StartupStates.Count(s => s.Enabled);
                PreviewChanges.Add($"Startup: restore {p.StartupStates.Count} entr{(p.StartupStates.Count == 1 ? "y" : "ies")} ({enabled} enabled, {p.StartupStates.Count - enabled} disabled)");
            }

            PreviewRequiresAdmin = optimizationNeedsAdmin || p.RegistrySettings.Any(r =>
                r.RequiresElevation || r.HkeyBase.Contains("LOCAL_MACHINE") || r.HkeyBase == "HKLM");

            PreviewSummary = PreviewChanges.Count == 0
                ? "This profile has no settings defined yet, so applying it won't change anything. Create a profile with optimizations ticked to make it actionable."
                : $"Applying '{p.Name}' bundles {p.Optimizations.Count} optimization(s){(p.StartupStates.Count > 0 ? $" and {p.StartupStates.Count} startup state(s)" : "")}. Registry changes are captured for Undo (Revert rolls them back; startup changes are not part of Undo).";

            OnPropertyChanged(nameof(PreviewHasChanges));
        }

        public string NewProfileName
        {
            get => _newProfileName;
            set { Set(ref _newProfileName, value); (CreateCommand as RelayCommand)?.OnCanExecuteChanged(); }
        }

        public ProfileType NewProfileType
        {
            get => _newProfileType;
            set => Set(ref _newProfileType, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                Set(ref _isBusy, value);
                (CreateCommand as RelayCommand)?.OnCanExecuteChanged();
                (ApplyCommand as RelayCommand)?.OnCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.OnCanExecuteChanged();
                (RevertCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand CreateCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand AddPresetCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ScheduleLogonCommand { get; }
        public ICommand RemoveScheduleCommand { get; }

        private void ScheduleLogon()
        {
            if (SelectedProfile == null) return;
            var ok = _scheduler.ScheduleOnLogon(SelectedProfile.Id, SelectedProfile.Name);
            StatusMessage = ok
                ? $"'{SelectedProfile.Name}' will be applied at every logon."
                : "Could not create the scheduled task (administrator rights may be required).";
        }

        private void RemoveSchedule()
        {
            if (SelectedProfile == null) return;
            var ok = _scheduler.RemoveSchedule(SelectedProfile.Name);
            StatusMessage = ok
                ? $"Removed the logon schedule for '{SelectedProfile.Name}'."
                : "No matching schedule was found to remove.";
        }

        public bool IsEditing => _editingId != null;
        public string CreateButtonText => IsEditing ? "Save changes" : "Create";
        public string NewProfileHeader => IsEditing ? "Edit Profile" : "New Profile";

        private void BeginEdit()
        {
            if (SelectedProfile == null) return;
            _editingId = SelectedProfile.Id;
            NewProfileName = SelectedProfile.Name;
            NewProfileType = SelectedProfile.ProfileType;
            CaptureStartupState = SelectedProfile.StartupStates.Count > 0;
            foreach (var choice in AvailableOptimizations)
            {
                choice.IsSelected = SelectedProfile.Optimizations.Contains(choice.Id);
            }
            RaiseEditingChanged();
            StatusMessage = $"Editing '{SelectedProfile.Name}'. Change the fields and click Save.";
        }

        private void CancelEdit()
        {
            _editingId = null;
            NewProfileName = string.Empty;
            CaptureStartupState = false;
            foreach (var choice in AvailableOptimizations) choice.IsSelected = false;
            RaiseEditingChanged();
            StatusMessage = "Edit cancelled.";
        }

        private void RaiseEditingChanged()
        {
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(CreateButtonText));
            OnPropertyChanged(nameof(NewProfileHeader));
            (EditCommand as RelayCommand)?.OnCanExecuteChanged();
            (CancelEditCommand as RelayCommand)?.OnCanExecuteChanged();
        }

        /// <summary>Imports a profile from a .json file path (used by drag-and-drop).</summary>
        public async Task ImportFromFileAsync(string path)
        {
            try
            {
                var imported = JsonSerializer.Deserialize<SettingsProfile>(await File.ReadAllTextAsync(path));
                if (imported == null || string.IsNullOrWhiteSpace(imported.Name))
                {
                    StatusMessage = $"'{Path.GetFileName(path)}' is not a valid profile.";
                    return;
                }
                var created = await _optimizerService.CreateProfileAsync(imported);
                await LoadAsync();
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == created.Id);
                StatusMessage = $"Imported profile '{created.Name}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
            }
        }

        private void LoadPresets()
        {
            Presets.Clear();
            foreach (var preset in _optimizerService.GetBuiltInPresets())
            {
                Presets.Add(preset);
            }
        }

        private async Task AddPresetAsync()
        {
            if (SelectedPreset == null) return;
            IsBusy = true;
            try
            {
                var created = await _optimizerService.CreateProfileAsync(new SettingsProfile
                {
                    Name = SelectedPreset.Name,
                    ProfileType = SelectedPreset.ProfileType,
                    Description = SelectedPreset.Description,
                    Optimizations = new List<string>(SelectedPreset.Optimizations)
                });
                await LoadAsync();
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == created.Id);
                StatusMessage = $"Added preset '{created.Name}' to your profiles.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not add preset: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Export()
        {
            if (SelectedProfile == null) return;
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export profile",
                    Filter = "Optimizer profile (*.json)|*.json",
                    FileName = $"{SanitizeFileName(SelectedProfile.Name)}.json"
                };
                if (dialog.ShowDialog() == true)
                {
                    var json = JsonSerializer.Serialize(SelectedProfile, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    StatusMessage = $"Exported '{SelectedProfile.Name}' to {dialog.FileName}.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Export failed: {ex.Message}";
            }
        }

        private async Task ImportAsync()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Import profile",
                    Filter = "Optimizer profile (*.json)|*.json|All files (*.*)|*.*"
                };
                if (dialog.ShowDialog() != true) return;

                var imported = JsonSerializer.Deserialize<SettingsProfile>(File.ReadAllText(dialog.FileName));
                if (imported == null || string.IsNullOrWhiteSpace(imported.Name))
                {
                    StatusMessage = "Import failed: the file is not a valid profile.";
                    return;
                }

                // CreateProfileAsync assigns a fresh id, so imports never collide with existing profiles.
                var created = await _optimizerService.CreateProfileAsync(imported);
                await LoadAsync();
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == created.Id);
                StatusMessage = $"Imported profile '{created.Name}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Import failed: {ex.Message}";
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return string.IsNullOrWhiteSpace(name) ? "profile" : name;
        }

        private async Task LoadAsync()
        {
            try
            {
                var profiles = await _optimizerService.ListProfilesAsync();
                Profiles.Clear();
                foreach (var profile in profiles)
                {
                    Profiles.Add(profile);
                }
                StatusMessage = $"{Profiles.Count} profile(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load profiles: {ex.Message}";
            }
        }

        private async Task LoadOptimizationChoicesAsync()
        {
            try
            {
                var ids = await _optimizerService.GetAvailableOptimizationsAsync();
                AvailableOptimizations.Clear();
                foreach (var id in ids)
                {
                    var info = _optimizerService.GetOptimizationInfo(id);
                    AvailableOptimizations.Add(new OptimizationChoice { Id = id, Title = info?.Title ?? id });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load optimizations: {ex.Message}";
            }
        }

        private async Task CreateAsync()
        {
            IsBusy = true;
            try
            {
                var selected = AvailableOptimizations.Where(o => o.IsSelected).Select(o => o.Id).ToList();

                var startupStates = new List<StartupState>();
                if (CaptureStartupState)
                {
                    startupStates = _startupService.GetEntries()
                        .Select(e => new StartupState { Name = e.Name, Location = e.Location, Enabled = e.Enabled })
                        .ToList();
                }

                string savedId;
                string verb;
                if (_editingId != null)
                {
                    // Editing an existing profile — preserve its id/created date.
                    var existing = Profiles.FirstOrDefault(p => p.Id == _editingId);
                    var updated = new SettingsProfile
                    {
                        Id = _editingId,
                        Name = NewProfileName.Trim(),
                        ProfileType = NewProfileType,
                        Description = $"{NewProfileType} profile",
                        Optimizations = selected,
                        StartupStates = CaptureStartupState ? startupStates : (existing?.StartupStates ?? new List<StartupState>()),
                        CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow
                    };
                    await _optimizerService.UpdateProfileAsync(updated);
                    savedId = _editingId;
                    verb = "Updated";
                }
                else
                {
                    var profile = await _optimizerService.CreateProfileAsync(new SettingsProfile
                    {
                        Name = NewProfileName.Trim(),
                        ProfileType = NewProfileType,
                        Description = $"{NewProfileType} profile",
                        Optimizations = selected,
                        StartupStates = startupStates
                    });
                    savedId = profile.Id;
                    verb = "Created";
                }

                _editingId = null;
                NewProfileName = string.Empty;
                CaptureStartupState = false;
                foreach (var choice in AvailableOptimizations)
                {
                    choice.IsSelected = false;
                }
                RaiseEditingChanged();
                await LoadAsync();
                SelectedProfile = Profiles.FirstOrDefault(p => p.Id == savedId);

                var parts = new List<string>();
                if (selected.Count > 0) parts.Add($"{selected.Count} optimization(s)");
                if (startupStates.Count > 0 && CaptureStartupState) parts.Add($"{startupStates.Count} startup entries captured");
                StatusMessage = parts.Count > 0
                    ? $"{verb} profile with {string.Join(" and ", parts)}."
                    : $"{verb} profile.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Create failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ApplyAsync()
        {
            if (SelectedProfile == null) return;
            IsBusy = true;
            try
            {
                var ok = await _optimizerService.ApplyProfileAsync(SelectedProfile.Id);
                StatusMessage = ok
                    ? $"Applied profile '{SelectedProfile.Name}'."
                    : $"Failed to apply '{SelectedProfile.Name}'.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Apply failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteAsync()
        {
            if (SelectedProfile == null) return;
            IsBusy = true;
            try
            {
                var name = SelectedProfile.Name;
                await _optimizerService.DeleteProfileAsync(SelectedProfile.Id);
                await LoadAsync();
                StatusMessage = $"Deleted profile '{name}'.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Delete failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RevertAsync()
        {
            if (SelectedProfile == null) return;
            IsBusy = true;
            try
            {
                await _optimizerService.RevertProfileAsync(SelectedProfile.Id);
                StatusMessage = $"Reverted changes from '{SelectedProfile.Name}'.";
                await LoadAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Revert failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>A tickable optimization shown when creating a profile.</summary>
    public class OptimizationChoice : Observable
    {
        private bool _isSelected;

        public string Id { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }
    }
}
