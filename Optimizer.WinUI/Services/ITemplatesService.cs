using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface ITemplatesService
{
    Task<IReadOnlyList<ConfigTemplate>> GetTemplatesAsync();
    Task<ConfigTemplate> CreateFromCurrentStateAsync(string name, string description);
    Task<string> ExportToDscAsync(ConfigTemplate template);
    Task<string> ExportToIntuneAsync(ConfigTemplate template);
    Task<string> ExportToWingetAsync(ConfigTemplate template);
    Task DeleteAsync(string id);
}
