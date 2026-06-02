using Optimizer.Helpers;

namespace Optimizer.ViewModels
{
    /// <summary>
    /// Shell view-model for the main page. Hosts the three tabbed sections
    /// (live dashboard, profile management, and resource history).
    /// </summary>
    public class MainViewModel : Observable
    {
        public MainViewModel(
            DashboardViewModel dashboard,
            ProfilesViewModel profiles,
            StartupViewModel startup,
            HistoryViewModel history,
            SettingsViewModel settings)
        {
            Dashboard = dashboard;
            Profiles = profiles;
            Startup = startup;
            History = history;
            Settings = settings;

            // Drill-down: clicking a dashboard metric jumps to the History tab (index 3).
            Dashboard.HistoryRequested += () => SelectedTabIndex = 3;
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => Set(ref _selectedTabIndex, value);
        }

        public DashboardViewModel Dashboard { get; }

        public ProfilesViewModel Profiles { get; }

        public StartupViewModel Startup { get; }

        public HistoryViewModel History { get; }

        public SettingsViewModel Settings { get; }
    }
}
