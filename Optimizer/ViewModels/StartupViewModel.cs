using System.Collections.ObjectModel;
using System.Windows.Input;

using Optimizer.Helpers;

using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace Optimizer.ViewModels
{
    public class StartupViewModel : Observable
    {
        private readonly IStartupService _startupService;
        private readonly IWindowsOptimizerService _optimizerService;
        private string _statusMessage = "Loading startup entries…";

        public StartupViewModel(IStartupService startupService, IWindowsOptimizerService optimizerService)
        {
            _startupService = startupService;
            _optimizerService = optimizerService;
            RefreshCommand = new RelayCommand(async () => await LoadAsync());
            _ = LoadAsync();
        }

        public ObservableCollection<StartupItemViewModel> Items { get; } = new();

        public bool IsElevated => _optimizerService.IsElevated;

        public bool ShowElevationNote => !_optimizerService.IsElevated;

        public string StatusMessage
        {
            get => _statusMessage;
            set => Set(ref _statusMessage, value);
        }

        public ICommand RefreshCommand { get; }

        private async Task LoadAsync()
        {
            StatusMessage = "Loading startup entries…";
            try
            {
                // Enumeration touches the registry, Task Scheduler and WMI — do it off the UI thread.
                var entries = await Task.Run(() => _startupService.GetEntries());
                Items.Clear();
                foreach (var entry in entries)
                {
                    Items.Add(new StartupItemViewModel(entry, _startupService, msg => StatusMessage = msg));
                }

                var enabled = entries.Count(e => e.Enabled);
                StatusMessage = $"{entries.Count} startup entr{(entries.Count == 1 ? "y" : "ies")} · {enabled} enabled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to read startup entries: {ex.Message}";
            }
        }
    }

    public class StartupItemViewModel : Observable
    {
        private readonly StartupEntry _entry;
        private readonly IStartupService _service;
        private readonly Action<string> _report;
        private bool _updating;

        public StartupItemViewModel(StartupEntry entry, IStartupService service, Action<string> report)
        {
            _entry = entry;
            _service = service;
            _report = report;
        }

        public string Name => _entry.Name;
        public string Command => _entry.Command;
        public string LocationText => _entry.LocationText;
        public bool RequiresAdmin => _entry.RequiresAdmin;

        public bool IsEnabled
        {
            get => _entry.Enabled;
            set
            {
                if (_updating || value == _entry.Enabled)
                {
                    return;
                }

                if (_service.SetEnabled(_entry, value))
                {
                    OnPropertyChanged(nameof(IsEnabled));
                    _report($"{(value ? "Enabled" : "Disabled")} '{_entry.Name}'.");
                }
                else
                {
                    // Revert the checkbox visually and explain why.
                    _report(_entry.RequiresAdmin
                        ? $"'{_entry.Name}' is a machine-wide entry — relaunch as administrator to change it."
                        : $"Could not change '{_entry.Name}'.");
                    _updating = true;
                    OnPropertyChanged(nameof(IsEnabled));
                    _updating = false;
                }
            }
        }
    }
}
