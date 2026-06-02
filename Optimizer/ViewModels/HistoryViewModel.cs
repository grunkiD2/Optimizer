using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Microsoft.Win32;

using Optimizer.Helpers;

using WindowsOptimizer.Models;
using WindowsOptimizer.Services;

namespace Optimizer.ViewModels
{
    public class HistoryViewModel : Observable
    {
        // Logical plot dimensions; the Canvas in the view uses the same coordinate space.
        public const double PlotWidth = 560;
        public const double PlotHeight = 140;

        private readonly SystemMonitorService _monitorService;
        private readonly DispatcherTimer _timer;

        private PointCollection _cpuPoints = new();
        private PointCollection _gpuPoints = new();
        private PointCollection _memoryPoints = new();
        private string _summary = "Collecting samples…";
        private string _stats = string.Empty;
        private bool _isRunning;
        private int _sampleRange = 60;

        public HistoryViewModel(SystemMonitorService monitorService)
        {
            _monitorService = monitorService;

            ToggleCommand = new RelayCommand(async () => await ToggleAsync());
            ExportCsvCommand = new RelayCommand(async () => await ExportCsvAsync());

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await RefreshAsync();

            _ = StartAsync();
        }

        public double PlotW => PlotWidth;
        public double PlotH => PlotHeight;

        public ObservableCollection<int> RangeOptions { get; } = new() { 30, 60, 120, 300 };

        public int SampleRange
        {
            get => _sampleRange;
            set { Set(ref _sampleRange, value); _ = RefreshAsync(); }
        }

        public string Stats
        {
            get => _stats;
            set => Set(ref _stats, value);
        }

        public PointCollection CpuPoints
        {
            get => _cpuPoints;
            set => Set(ref _cpuPoints, value);
        }

        public PointCollection GpuPoints
        {
            get => _gpuPoints;
            set => Set(ref _gpuPoints, value);
        }

        public PointCollection MemoryPoints
        {
            get => _memoryPoints;
            set => Set(ref _memoryPoints, value);
        }

        public string Summary
        {
            get => _summary;
            set => Set(ref _summary, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { Set(ref _isRunning, value); OnPropertyChanged(nameof(ToggleText)); }
        }

        public string ToggleText => IsRunning ? "Pause" : "Resume";

        public ICommand ToggleCommand { get; }

        public ICommand ExportCsvCommand { get; }

        private async Task StartAsync()
        {
            IsRunning = true;
            _timer.Start();
            // Fire-and-forget the long-running sampler; it self-limits to its rolling buffer.
            _ = _monitorService.StartMonitoringAsync(int.MaxValue / 1000);
            await RefreshAsync();
        }

        private Task ToggleAsync()
        {
            if (IsRunning)
            {
                _timer.Stop();
                _monitorService.StopMonitoring();
                IsRunning = false;
            }
            else
            {
                _ = StartAsync();
            }
            return Task.CompletedTask;
        }

        private async Task RefreshAsync()
        {
            try
            {
                var history = (await _monitorService.GetResourceHistoryAsync(SampleRange))
                    .OrderBy(s => s.Timestamp)
                    .ToList();

                if (history.Count == 0)
                {
                    return;
                }

                CpuPoints = BuildSeries(history, s => s.CpuUsagePercentage);
                GpuPoints = BuildSeries(history, s => s.GpuUsagePercentage);
                MemoryPoints = BuildSeries(history, MemoryPercent);

                var latest = history[^1];
                Summary = $"{history.Count} samples · CPU {latest.CpuUsagePercentage:F0}% · " +
                          $"Mem {MemoryPercent(latest):F0}% · GPU {latest.GpuUsagePercentage:F0}% · " +
                          $"Disk {latest.DiskReadSpeed / 1024:F0}↓/{latest.DiskWriteSpeed / 1024:F0}↑ KB/s · " +
                          $"Net {latest.NetworkInSpeed / 1024:F0}↓/{latest.NetworkOutSpeed / 1024:F0}↑ KB/s";

                Stats = $"CPU avg {history.Average(s => s.CpuUsagePercentage):F0}% / max {history.Max(s => s.CpuUsagePercentage):F0}%   ·   " +
                        $"Mem avg {history.Average(MemoryPercent):F0}% / max {history.Max(MemoryPercent):F0}%   ·   " +
                        $"GPU avg {history.Average(s => s.GpuUsagePercentage):F0}% / max {history.Max(s => s.GpuUsagePercentage):F0}%";
            }
            catch (Exception ex)
            {
                Summary = $"History error: {ex.Message}";
            }
        }

        private async Task ExportCsvAsync()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export history",
                    Filter = "CSV (*.csv)|*.csv",
                    FileName = "optimizer-history.csv"
                };
                if (dialog.ShowDialog() != true) return;

                var history = (await _monitorService.GetResourceHistoryAsync(int.MaxValue))
                    .OrderBy(s => s.Timestamp)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,CPU%,Mem%,GPU%,TotalMemMB,AvailMemMB,GpuMemMB,CpuTempC,GpuTempC,DiskReadKBps,DiskWriteKBps,NetInKBps,NetOutKBps");
                foreach (var s in history)
                {
                    sb.AppendLine(string.Join(",",
                        s.Timestamp.ToString("s", CultureInfo.InvariantCulture),
                        s.CpuUsagePercentage.ToString("F1", CultureInfo.InvariantCulture),
                        MemoryPercent(s).ToString("F1", CultureInfo.InvariantCulture),
                        s.GpuUsagePercentage.ToString("F1", CultureInfo.InvariantCulture),
                        (s.TotalPhysicalMemory / 1024 / 1024).ToString(CultureInfo.InvariantCulture),
                        (s.AvailablePhysicalMemory / 1024 / 1024).ToString(CultureInfo.InvariantCulture),
                        (s.GpuMemoryUsage / 1024 / 1024).ToString(CultureInfo.InvariantCulture),
                        s.CpuTemperature.ToString("F1", CultureInfo.InvariantCulture),
                        s.GpuTemperature.ToString("F1", CultureInfo.InvariantCulture),
                        (s.DiskReadSpeed / 1024).ToString("F0", CultureInfo.InvariantCulture),
                        (s.DiskWriteSpeed / 1024).ToString("F0", CultureInfo.InvariantCulture),
                        (s.NetworkInSpeed / 1024).ToString("F0", CultureInfo.InvariantCulture),
                        (s.NetworkOutSpeed / 1024).ToString("F0", CultureInfo.InvariantCulture)));
                }

                await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                Summary = $"Exported {history.Count} samples to {dialog.FileName}.";
            }
            catch (Exception ex)
            {
                Summary = $"Export failed: {ex.Message}";
            }
        }

        private static double MemoryPercent(SystemResource s)
        {
            if (s.TotalPhysicalMemory <= 0) return 0;
            return (s.TotalPhysicalMemory - s.AvailablePhysicalMemory) * 100.0 / s.TotalPhysicalMemory;
        }

        private static PointCollection BuildSeries(IReadOnlyList<SystemResource> history, Func<SystemResource, double> selector)
        {
            var points = new PointCollection();
            var count = history.Count;
            if (count == 1)
            {
                var y = ToY(selector(history[0]));
                points.Add(new Point(0, y));
                points.Add(new Point(PlotWidth, y));
                return points;
            }

            for (var i = 0; i < count; i++)
            {
                var x = i / (double)(count - 1) * PlotWidth;
                var y = ToY(selector(history[i]));
                points.Add(new Point(x, y));
            }
            return points;
        }

        private static double ToY(double percent)
        {
            var clamped = Math.Max(0, Math.Min(100, percent));
            return PlotHeight - (clamped / 100.0 * PlotHeight);
        }
    }
}
