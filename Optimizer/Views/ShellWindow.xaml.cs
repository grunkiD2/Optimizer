using System;
using System.Windows;
using System.Windows.Controls;

using Optimizer.Contracts.Services;
using Optimizer.Contracts.Views;
using Optimizer.ViewModels;

using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared;

namespace Optimizer.Views
{
    public partial class ShellWindow : ChromelessWindow, IShellWindow
    {
        public string themeName = App.Current.Properties["Theme"]?.ToString() != null ? App.Current.Properties["Theme"]?.ToString() : "Windows11Light";

        private Frame _shellFrame;

        public ShellWindow(IPageService pageService, ShellViewModel viewModel)
        {
            // Load XAML manually if InitializeComponent fails
            try
            {
                InitializeComponent();
            }
            catch
            {
                // Fallback: manually create UI
                this.Title = "Optimizer";
                this.Width = 1200;
                this.Height = 800;
                this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                var grid = new Grid();
                _shellFrame = new Frame { Name = "shellFrame" };
                grid.Children.Add(_shellFrame);
                this.Content = grid;
            }

            DataContext = viewModel;

            // Get frame reference
            if (_shellFrame == null)
            {
                _shellFrame = (Frame)FindName("shellFrame");
            }

            SfSkinManager.SetTheme(this, new Theme(themeName));
        }

        public Frame GetNavigationFrame()
            => _shellFrame;

        public void ShowWindow()
            => Show();

        public void CloseWindow()
            => Close();
    }
}
