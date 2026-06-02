using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IRecommendationsService
{
    Task<IReadOnlyList<Recommendation>> GenerateAsync();
    Task DismissAsync(string id);
    Task ResetDismissedAsync();
}
