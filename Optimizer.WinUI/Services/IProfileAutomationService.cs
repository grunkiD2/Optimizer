using Optimizer.WinUI.Models;

namespace Optimizer.WinUI.Services;

public interface IProfileAutomationService
{
    IReadOnlyList<ProfileRule> Rules { get; }
    Task AddRuleAsync(ProfileRule rule);
    Task UpdateRuleAsync(ProfileRule rule);
    Task DeleteRuleAsync(string ruleId);
    void Start();
    void Stop();
}
