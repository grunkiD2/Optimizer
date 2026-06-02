using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Microsoft.Win32;

using Optimizer.ViewModels;

namespace Optimizer.Views
{
    public partial class MainPage : Page
    {
        public MainPage(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        // Imports profiles dropped (.json files) onto the profiles list.
        private async void ProfilesList_Drop(object sender, DragEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files.Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                await vm.Profiles.ImportFromFileAsync(file);
            }
        }

        // Renders the history chart canvas to a PNG. Kept in code-behind because it needs the visual.
        private void ExportPng_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var width = (int)(HistoryPlot.ActualWidth > 0 ? HistoryPlot.ActualWidth : HistoryPlot.Width);
                var height = (int)(HistoryPlot.ActualHeight > 0 ? HistoryPlot.ActualHeight : HistoryPlot.Height);
                if (width <= 0 || height <= 0)
                {
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Export chart",
                    Filter = "PNG image (*.png)|*.png",
                    FileName = "optimizer-history.png"
                };
                if (dialog.ShowDialog() != true)
                {
                    return;
                }

                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(HistoryPlot);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to export history PNG");
            }
        }
    }
}
