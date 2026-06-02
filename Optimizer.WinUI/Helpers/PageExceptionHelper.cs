using Microsoft.UI.Xaml;
using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Helpers;

public static class PageExceptionHelper
{
    /// <summary>
    /// Wraps an async action with exception logging and optional user-facing dialog.
    /// Use from async void event handlers to prevent unhandled exceptions from crashing the app.
    /// </summary>
    public static async Task SafeAsync(Func<Task> action, XamlRoot? root = null, string? contextLabel = null)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // User cancelled — no notification needed
        }
        catch (Exception ex)
        {
            EngineLog.Error($"Unhandled exception in {contextLabel ?? "event handler"}", ex);

            if (root != null)
            {
                try
                {
                    await DialogHelper.ConfirmAsync(
                        root,
                        "Something went wrong",
                        $"{contextLabel ?? "Operation"} failed: {ex.Message}",
                        "OK");
                }
                catch { /* dialog itself failed; nothing to do */ }
            }
        }
    }
}
