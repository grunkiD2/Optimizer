using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Optimizer.Helpers;
using Optimizer.Services;

using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace Optimizer.ViewModels
{
    public class DashboardViewModel : Observable
    {
        private readonly IWindowsOptimizerService _optimizerService;
        private readonly IElevationService _elevationService;
        private readonly SystemMonitorService _monitorService;
        private readonly IProcessService _processService;
        private readonly INotificationService _notifications;
        private readonly DispatcherTimer _metricsTimer;

        private double _cpuUsage;
        private double _memoryPercent;
        private double _gpuUsage;
        private int _totalProcessors;
        private string _memoryText = "—";
        private string _diskText = "—";
        private string _networkText = "—";
        private string _lastUpdated = "Never";
        private string _selectedOptimization;
        private OptimizationInfo _selectedInfo;
        private string _statusMessage = "Ready.";
        private bool _isBusy;
        private int _pendingUndoCount;
        private int _healthScore = 100;

        public DashboardViewModel(
            IWindowsOptimizerService optimizerService,
            IElevationService elevationService,
            ISettingsService settingsService,
            SystemMonitorService monitorService,
            IProcessService processService,
            INotificationService notifications)
        {
            _optimizerService = optimizerService;
            _elevationService = elevationService;
            _monitorService = monitorService;
            _processService = processService;
            _notifications = notifications;
            RefreshUndoEntries();
            var refreshSeconds = Math.Max(1, settingsService.Settings.MetricsRefreshSeconds);

            RefreshCommand = new RelayCommand(async () => await RefreshMetricsAsync());
            ApplyCommand = new RelayCommand(async () => await ApplySelectedOptimizationAsync(),
                () => !IsBusy && !string.IsNullOrEmpty(SelectedOptimization));
            ApplySafeCommand = new RelayCommand(async () => await ApplySafeAsync(), () => !IsBusy);
            UndoAllCommand = new RelayCommand(async () => await UndoAllAsync(),
                () => !IsBusy && PendingUndoCount > 0);
            UndoEntryCommand = new RelayCommand<UndoEntry>(async e => await UndoEntryAsync(e), _ => !IsBusy);
            RelaunchAsAdminCommand = new RelayCommand(RelaunchAsAdmin, () => !IsElevated);
            KillProcessCommand = new RelayCommand<ProcessInfo>(KillProcess, _ => !IsBusy);
            ShowHistoryCommand = new RelayCommand(() => HistoryRequested?.Invoke());

            _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(refreshSeconds) };
            _metricsTimer.Tick += async (_, _) => await RefreshMetricsAsync();
            _metricsTimer.Start();

            _ = InitializeAsync();
        }

        /// <summary>Raised when the user clicks a metric to drill into the History tab.</summary>
        public event Action HistoryRequested;

        public ObservableCollection<string> Optimizations { get; } = new();

        /// <summary>Individual reversible changes, most-recent first.</summary>
        public ObservableCollection<UndoEntry> UndoEntries { get; } = new();

        /// <summary>Per-logical-core CPU usage bars.</summary>
        public ObservableCollection<CoreUsageItem> CoreUsages { get; } = new();

        /// <summary>Top processes by memory.</summary>
        public ObservableCollection<ProcessInfo> TopProcesses { get; } = new();

        public string DiskText
        {
            get => _diskText;
            set => Set(ref _diskText, value);
        }

        public string NetworkText
        {
            get => _networkText;
            set => Set(ref _networkText, value);
        }

        public int HealthScore
        {
            get => _healthScore;
            set { Set(ref _healthScore, value); OnPropertyChanged(nameof(HealthText)); OnPropertyChanged(nameof(HealthBrush)); }
        }

        public string HealthText => HealthScore >= 80 ? $"System health: Good ({HealthScore})"
            : HealthScore >= 50 ? $"System health: Fair ({HealthScore})"
            : $"System health: Needs attention ({HealthScore})";

        public string HealthBrush => HealthScore >= 80 ? "#FFDCFCE7" : HealthScore >= 50 ? "#FFFEF3C7" : "#FFFFE4E6";

        public bool IsElevated => _optimizerService.IsElevated;

        public bool ShowElevationWarning => !_optimizerService.IsElevated;

        public string ElevationText => _optimizerService.IsElevated
            ? "Running as administrator — all optimizations available."
            : "Running without administrator rights — system-wide tweaks (HKLM, network) are disabled.";

        public double CpuUsage
        {
            get => _cpuUsage;
            set { Set(ref _cpuUsage, value); OnPropertyChanged(nameof(CpuText)); }
        }

        public string CpuText => $"{CpuUsage:F1} %";

        public double MemoryPercent
        {
            get => _memoryPercent;
            set => Set(ref _memoryPercent, value);
        }

        public string MemoryText
        {
            get => _memoryText;
            set => Set(ref _memoryText, value);
        }

        public double GpuUsage
        {
            get => _gpuUsage;
            set { Set(ref _gpuUsage, value); OnPropertyChanged(nameof(GpuText)); }
        }

        public string GpuText => $"{GpuUsage:F1} %";

        public int TotalProcessors
        {
            get => _totalProcessors;
            set => Set(ref _totalProcessors, value);
        }

        public string LastUpdated
        {
            get => _lastUpdated;
            set => Set(ref _lastUpdated, value);
        }

        public string SelectedOptimization
        {
            get => _selectedOptimization;
            set
            {
                Set(ref _selectedOptimization, value);
                SelectedInfo = string.IsNullOrEmpty(value) ? null : _optimizerService.GetOptimizationInfo(value);
                (ApplyCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }

        public OptimizationInfo SelectedInfo
        {
            get => _selectedInfo;
            set
            {
                Set(ref _selectedInfo, value);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(NotReversible));
            }
        }

        public bool HasSelection => _selectedInfo != null;

        /// <summary>True when the selected optimization cannot be undone (drives a warning in the UI).</summary>
        public bool NotReversible => _selectedInfo is { Reversible: false };

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
                (ApplyCommand as RelayCommand)?.OnCanExecuteChanged();
                (UndoAllCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }

        public int PendingUndoCount
        {
            get => _pendingUndoCount;
            set
            {
                Set(ref _pendingUndoCount, value);
                OnPropertyChanged(nameof(UndoText));
                OnPropertyChanged(nameof(HasUndoEntries));
                (UndoAllCommand as RelayCommand)?.OnCanExecuteChanged();
            }
        }

        public bool HasUndoEntries => PendingUndoCount > 0;

        public string UndoText => PendingUndoCount > 0
            ? $"Undo all ({PendingUndoCount})"
            : "Nothing to undo";

        public ICommand RefreshCommand { get; }

        public ICommand ApplyCommand { get; }

        public ICommand UndoAllCommand { get; }

        public ICommand UndoEntryCommand { get; }

        public ICommand ApplySafeCommand { get; }

        public ICommand KillProcessCommand { get; }

        public ICommand ShowHistoryCommand { get; }

        public ICommand RelaunchAsAdminCommand { get; }

        private void KillProcess(ProcessInfo process)
        {
            if (process == null) return;
            StatusMessage = _processService.KillProcess(process.Pid)
                ? $"Ended {process.Name} (PID {process.Pid})."
                : $"Could not end {process.Name} (PID {process.Pid}) — may need administrator.";
        }

        private async Task ApplySafeAsync()
        {
            IsBusy = true;
            StatusMessage = "Applying safe optimizations…";
            try
            {
                var applied = 0;
                foreach (var id in Optimizations.ToList())
                {
                    var info = _optimizerService.GetOptimizationInfo(id);
                    // Only the low-risk, reversible, no-admin tweaks.
                    if (info is { RequiresAdmin: false, Reversible: true })
                    {
                        var result = await _optimizerService.ApplyOptimizationAsync(id);
                        if (result.Success) applied++;
                    }
                }
                RefreshUndoEntries();
                StatusMessage = $"Applied {applied} safe optimization(s). Use Undo to revert any.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Apply-safe failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Re-reads the undo log into the list and updates the count.</summary>
        private void RefreshUndoEntries()
        {
            UndoEntries.Clear();
            foreach (var entry in _optimizerService.GetUndoEntries().Reverse())
            {
                UndoEntries.Add(entry);
            }
            PendingUndoCount = UndoEntries.Count;
        }

        private async Task UndoEntryAsync(UndoEntry entry)
        {
            if (entry == null) return;
            IsBusy = true;
            try
            {
                var ok = await _optimizerService.UndoEntryAsync(entry);
                StatusMessage = ok ? $"Reverted: {entry.Description}" : $"Could not revert: {entry.Description}";
                RefreshUndoEntries();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Undo failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void RelaunchAsAdmin()
        {
            // Starts an elevated copy via UAC; if it launched, close this (non-elevated) instance.
            if (_elevationService.TryRelaunchElevated())
            {
                Application.Current.Shutdown();
            }
            else
            {
                StatusMessage = "Could not relaunch as administrator (the UAC prompt may have been declined).";
            }
        }

        private async Task InitializeAsync()
        {
            try
            {
                var optimizations = await _optimizerService.GetAvailableOptimizationsAsync();
                Optimizations.Clear();
                foreach (var optimization in optimizations)
                {
                    Optimizations.Add(optimization);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load optimizations: {ex.Message}";
            }

            await RefreshMetricsAsync();
        }

        private async Task RefreshMetricsAsync()
        {
            try
            {
                var snapshot = await Task.Run(() => _optimizerService.GetCurrentResourceUsage());
                var cores = await Task.Run(() => _monitorService.GetPerCoreUsage());
                var processes = await Task.Run(() => _processService.GetTopProcesses(8));

                CpuUsage = snapshot.CpuUsagePercentage;
                GpuUsage = snapshot.GpuUsagePercentage;
                TotalProcessors = snapshot.TotalProcessors;

                if (snapshot.TotalPhysicalMemory > 0)
                {
                    var usedBytes = snapshot.TotalPhysicalMemory - snapshot.AvailablePhysicalMemory;
                    MemoryPercent = usedBytes * 100.0 / snapshot.TotalPhysicalMemory;
                    MemoryText = $"{ToGigabytes(usedBytes):F1} / {ToGigabytes(snapshot.TotalPhysicalMemory):F1} GB";
                }
                else
                {
                    MemoryPercent = 0;
                    MemoryText = "Unavailable";
                }

                DiskText = $"{snapshot.DiskReadSpeed / 1024:F0}↓ / {snapshot.DiskWriteSpeed / 1024:F0}↑ KB/s";
                NetworkText = $"{snapshot.NetworkInSpeed / 1024:F0}↓ / {snapshot.NetworkOutSpeed / 1024:F0}↑ KB/s";

                // Per-core bars (update in place to avoid collection churn).
                if (CoreUsages.Count != cores.Count)
                {
                    CoreUsages.Clear();
                    foreach (var c in cores) CoreUsages.Add(new CoreUsageItem { Value = c });
                }
                else
                {
                    for (var i = 0; i < cores.Count; i++) CoreUsages[i].Value = cores[i];
                }

                // Top processes.
                TopProcesses.Clear();
                foreach (var p in processes) TopProcesses.Add(p);

                // Simple health score from current load.
                HealthScore = (int)Math.Round(Math.Clamp(100 - (CpuUsage * 0.5 + MemoryPercent * 0.5), 0, 100));

                LastUpdated = snapshot.Timestamp.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Metrics error: {ex.Message}";
            }
        }

        private async Task ApplySelectedOptimizationAsync()
        {
            if (string.IsNullOrEmpty(SelectedOptimization))
            {
                return;
            }

            IsBusy = true;
            StatusMessage = $"Applying '{SelectedOptimization}'…";
            try
            {
                var result = await _optimizerService.ApplyOptimizationAsync(SelectedOptimization);
                StatusMessage = result.Success
                    ? $"✓ {result.Message}"
                    : $"✗ {result.Message} {string.Join("; ", result.Errors)}";

                if (result.Warnings.Count > 0)
                {
                    StatusMessage += $" (Warnings: {string.Join("; ", result.Warnings)})";
                }

                _notifications.Notify("Optimization", result.Success ? result.Message : $"Failed: {result.Message}", !result.Success);
                RefreshUndoEntries();
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

        private async Task UndoAllAsync()
        {
            IsBusy = true;
            StatusMessage = "Reverting changes…";
            try
            {
                var restored = await _optimizerService.UndoAllOptimizationsAsync();
                RefreshUndoEntries();
                StatusMessage = $"Reverted {restored} change(s).";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Undo failed: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static double ToGigabytes(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;
    }

    /// <summary>A single per-core usage value that updates in place (smooth bars, no list churn).</summary>
    public class CoreUsageItem : Observable
    {
        private double _value;
        public double Value
        {
            get => _value;
            set => Set(ref _value, value);
        }
    }
}
