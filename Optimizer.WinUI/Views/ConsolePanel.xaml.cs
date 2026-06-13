using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Optimizer.WinUI.Helpers;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.ViewModels;
using Windows.System;

namespace Optimizer.WinUI.Views;

public sealed partial class ConsolePanel : UserControl
{
    public ConsoleViewModel ConsoleVM { get; } = App.GetService<ConsoleViewModel>();
    public AssistantViewModel AssistantVM { get; } = App.GetService<AssistantViewModel>();

    public event EventHandler? PopOutRequested;
    public event EventHandler? CollapseRequested;

    public ConsolePanel()
    {
        InitializeComponent();
        AssistantVM.ConfirmHandler = ConfirmAsync;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Resources["LivePulse"] is Storyboard sb)
            sb.Begin();
    }

    /// <summary>Switch to the Assistant tab and focus the input box.</summary>
    public void FocusAssistant()
    {
        ConsoleTabs.SelectedIndex = 1;
        InputBox.Focus(FocusState.Programmatic);
    }

    private void ConsoleTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: during InitializeComponent the panes may not yet be named.
        if (PaneActivity is null) return;

        var i = ConsoleTabs.SelectedIndex;
        PaneActivity.Visibility  = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PaneAssistant.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<bool> ConfirmAsync(string id, string summary)
    {
        var dialog = new ContentDialog
        {
            Title = "Confirm action",
            Content = $"The assistant wants to run:\n\n{summary}\n\nThis will change your system (reversible via undo). Allow it?",
            PrimaryButtonText = "Allow",
            CloseButtonText = "Decline",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void Input_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && AssistantVM.SendCommand.CanExecute(null))
        {
            AssistantVM.SendCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void PopOut_Click(object sender, RoutedEventArgs e)   => PopOutRequested?.Invoke(this, EventArgs.Empty);
    private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);

    // ── Console / chat context menus (Batch 3) ───────────────────────────────

    private void ActivityCopyLine_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ConsoleLine line)
            RowActions.CopyText($"{line.TimeText} {line.Glyph} {line.Text}");
    }

    private void ActivityCopyAll_Click(object sender, RoutedEventArgs e)
        => RowActions.CopyText(string.Join(Environment.NewLine,
            ConsoleVM.Lines.Select(l => $"{l.TimeText} {l.Glyph} {l.Text}")));

    private async void ActivitySaveLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureFolderExists();
            var path = AppPaths.GetDataFile($"activity-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var text = string.Join(Environment.NewLine,
                ConsoleVM.Lines.Select(l => $"{l.TimeText} {l.Glyph} {l.Text}"));
            await System.IO.File.WriteAllTextAsync(path, text);
            RowActions.RevealInExplorer(path);
        }
        catch (Exception ex)
        {
            await DialogHelper.InfoAsync(this.XamlRoot, "Gem log", $"Kunne ikke gemme loggen: {ex.Message}");
        }
    }

    private void ChatCopy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ChatMessage msg)
            RowActions.CopyText(msg.Text);
    }

    private void ChatCopyAll_Click(object sender, RoutedEventArgs e)
        => RowActions.CopyText(string.Join(Environment.NewLine + Environment.NewLine,
            AssistantVM.Messages.Select(m => m.Text)));
}
