using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinRT.Interop;

namespace Optimizer.WinUI.Helpers;

/// <summary>
/// Makes the mouse wheel scroll whatever ScrollViewer is under the cursor — app-wide.
///
/// Two WinUI 3 problems are corrected:
///  1) Wheel input is routed to the FOCUSED scrollable, not the hovered one.
///  2) On multi-monitor / mixed-DPI setups the pointer position WinUI reports is offset from
///     the real cursor, so the wheel scrolls a control to one side of where the mouse actually is.
///
/// We attach once at a window's root content, take the TRUE cursor position from Win32
/// (GetCursorPos → ScreenToClient → ÷ rasterization scale), hit-test for the ScrollViewer under
/// it, and scroll that one.
/// </summary>
public static class WheelScrollRouter
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }

    /// <summary>Attach to a window so the wheel follows the cursor across all its pages/controls.</summary>
    public static void Attach(Window window)
    {
        if (window.Content is not UIElement root) return;
        var hwnd = WindowNative.GetWindowHandle(window);
        root.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((s, e) => OnWheel(root, hwnd, e)), handledEventsToo: true);
    }

    private static void OnWheel(UIElement root, IntPtr hwnd, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(root).Properties.MouseWheelDelta;
        if (delta == 0) return;

        // Use the REAL cursor position (physical screen px → client px → DIPs), not WinUI's
        // reported pointer position, which can be offset on multi-monitor / mixed-DPI setups.
        if (!GetCursorPos(out var p)) return;
        ScreenToClient(hwnd, ref p);
        var scale = root.XamlRoot?.RasterizationScale ?? 1.0;
        var pos = new Point(p.X / scale, p.Y / scale);

        var hovered = HitTestScrollViewer(root, pos);
        if (hovered is null) return;

        // Don't double-scroll: if WinUI already scrolled the same element (it was focused), skip.
        var focused = FindAncestorScrollViewer(
            FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject);
        if (e.Handled && ReferenceEquals(focused, hovered)) return;

        // Wheel up (positive delta) scrolls content up → decrease the vertical offset.
        hovered.ChangeView(null, hovered.VerticalOffset - delta, null, disableAnimation: true);
        e.Handled = true;
    }

    private static ScrollViewer? HitTestScrollViewer(UIElement root, Point position)
    {
        foreach (var el in VisualTreeHelper.FindElementsInHostCoordinates(position, root))
        {
            var sv = el as ScrollViewer ?? FindAncestorScrollViewer(el as DependencyObject);
            if (sv is not null && sv.ScrollableHeight > 0) return sv;
        }
        return null;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is ScrollViewer sv) return sv;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }
}
