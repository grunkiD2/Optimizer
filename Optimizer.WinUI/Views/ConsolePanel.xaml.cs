using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Optimizer.WinUI.ViewModels;
using Windows.System;

namespace Optimizer.WinUI.Views;

public sealed partial class ConsolePanel : UserControl
{
    public ConsoleViewModel ConsoleVM { get; } = App.GetService<ConsoleViewModel>();
    public AssistantViewModel AssistantVM { get; } = App.GetService<AssistantViewModel>();

    /// <summary>Raised when the user clicks pop-out / collapse so the host (MainWindow) can react.</summary>
    public event EventHandler? PopOutRequested;
    public event EventHandler? CollapseRequested;

    public ConsolePanel()
    {
        InitializeComponent();
        AssistantVM.ConfirmHandler = ConfirmAsync;

        // Register wheel handlers with handledEventsToo:true. WinUI 3's inner ListView
        // ScrollViewer marks PointerWheelChanged as handled even when it fails to scroll
        // (the dock/secondary-window wheel bug), so a normal XAML handler never fires.
        // handledEventsToo lets ours run regardless and drive the ScrollViewer directly.
        AssistantList.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(List_PointerWheelChanged), handledEventsToo: true);
        ActivityList.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(List_PointerWheelChanged), handledEventsToo: true);
    }

    public void FocusAssistant()
    {
        // Both panes are always visible now; just focus the chat input.
        InputBox.Focus(FocusState.Programmatic);
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

    private void PopOut_Click(object sender, RoutedEventArgs e) => PopOutRequested?.Invoke(this, EventArgs.Empty);
    private void Collapse_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Manually drive the list's ScrollViewer on mouse wheel. WinUI 3 does not reliably route
    /// wheel input to ScrollViewers hosted in the dock/secondary console window, so the built-in
    /// scrolling appears dead even though the ScrollViewer itself works. This restores it.
    /// </summary>
    private void List_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element) return;
        var scrollViewer = FindScrollViewer(element);
        if (scrollViewer is null) return;

        var delta = e.GetCurrentPoint(element).Properties.MouseWheelDelta;
        // Wheel up (positive delta) scrolls content up → decrease the vertical offset.
        scrollViewer.ChangeView(null, scrollViewer.VerticalOffset - delta, null, disableAnimation: true);
        e.Handled = true;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }
}
