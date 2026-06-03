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
    }

    /// <summary>
    /// WinUI routes the mouse wheel to the FOCUSED scrollable element, not the one under the
    /// cursor — so the console lists only scroll once they have focus. Give the hovered list
    /// focus so wheeling over it scrolls it (matching what users expect).
    /// </summary>
    private void List_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Control list)
            list.Focus(FocusState.Pointer);
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
}
