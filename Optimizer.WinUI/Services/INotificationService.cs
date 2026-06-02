namespace Optimizer.WinUI.Services;

public enum NotificationCategory
{
    Performance,
    Storage,
    Hardware,
    Security,
    Recommendations,
    OptimizationApplied
}

public interface INotificationService
{
    void Show(string title, string message, NotificationCategory category, string? actionUri = null);
    bool IsCategoryEnabled(NotificationCategory category);
}
