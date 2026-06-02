using Microsoft.Windows.AppNotifications;

namespace Optimizer.WinUI.Services;

public class NotificationService : INotificationService
{
    private readonly ISettingsService _settings;
    private readonly Dictionary<NotificationCategory, DateTime> _lastShown = [];
    private readonly TimeSpan _cooldown = TimeSpan.FromMinutes(15);

    public NotificationService(ISettingsService settings) => _settings = settings;

    public bool IsCategoryEnabled(NotificationCategory category) => category switch
    {
        NotificationCategory.Performance     => _settings.Settings.NotifyPerformance,
        NotificationCategory.Storage         => _settings.Settings.NotifyStorage,
        NotificationCategory.Hardware        => _settings.Settings.NotifyHardware,
        NotificationCategory.Security        => _settings.Settings.NotifySecurity,
        NotificationCategory.Recommendations => _settings.Settings.NotifyRecommendations,
        NotificationCategory.OptimizationApplied => _settings.Settings.NotifyOptimizations,
        _ => false
    };

    public void Show(string title, string message, NotificationCategory category, string? actionUri = null)
    {
        if (!IsCategoryEnabled(category)) return;

        // Cooldown per category — avoid spam
        if (_lastShown.TryGetValue(category, out var last) && DateTime.Now - last < _cooldown)
            return;
        _lastShown[category] = DateTime.Now;

        try
        {
            var safeTitle   = System.Security.SecurityElement.Escape(title)   ?? title;
            var safeMessage = System.Security.SecurityElement.Escape(message) ?? message;

            var xml = $"""
                <toast>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{safeTitle}</text>
                      <text>{safeMessage}</text>
                    </binding>
                  </visual>
                </toast>
                """;

            AppNotificationManager.Default.Show(new AppNotification(xml));
        }
        catch (Exception ex)
        {
            EngineLog.Error("Toast notification failed", ex);
        }
    }
}
