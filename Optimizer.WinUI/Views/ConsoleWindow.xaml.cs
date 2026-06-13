using System;
using Microsoft.UI.Xaml;

namespace Optimizer.WinUI.Views;

public sealed partial class ConsoleWindow : Window
{
    public event EventHandler? ReDockRequested;

    public ConsoleWindow()
    {
        InitializeComponent();
        var panel = new ConsolePanel();
        panel.CollapseRequested += (_, _) => Close();
        // Batch 4a: the pop-out's own "Pop out" button was dead. In a floating window it acts
        // as re-dock — close the window, which fires ReDockRequested and restores the dock.
        panel.PopOutRequested += (_, _) => Close();
        HostGrid.Children.Add(panel);
        Closed += (_, _) => ReDockRequested?.Invoke(this, EventArgs.Empty);
    }
}
