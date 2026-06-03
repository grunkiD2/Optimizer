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
        HostGrid.Children.Add(panel);
        Closed += (_, _) => ReDockRequested?.Invoke(this, EventArgs.Empty);
    }
}
