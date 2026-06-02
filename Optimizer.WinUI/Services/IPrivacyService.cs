using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Services;

public partial class PrivacySetting : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string name = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private bool isPrivacyFriendly;
}

public interface IPrivacyService
{
    Task<IReadOnlyList<PrivacySetting>> GetAllAsync();
    Task<bool> SetEnabledAsync(string id, bool enableForPrivacy);
}
