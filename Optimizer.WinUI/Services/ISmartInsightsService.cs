using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ISmartInsightsService
{
    Task<IReadOnlyList<SmartInsight>> GenerateAsync();
}
