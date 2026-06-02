namespace Optimizer.WinUI.Services;

public interface IThemeService
{
    void ApplyTheme(string theme);
    void ApplyBackdrop(string material);
    void Initialize(Microsoft.UI.Xaml.Window window);
}
