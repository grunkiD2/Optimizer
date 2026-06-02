namespace Optimizer.WinUI.Models;

/// <summary>Transient state accumulated during the first-launch onboarding wizard.</summary>
public class OnboardingState
{
    public string SelectedUsage   { get; set; } = "Mixed";
    public string SelectedPrivacy { get; set; } = "Balanced";
}
