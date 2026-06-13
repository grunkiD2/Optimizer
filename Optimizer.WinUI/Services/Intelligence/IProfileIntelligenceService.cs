// IProfileIntelligenceService.cs
namespace Optimizer.WinUI.Services.Intelligence;

public interface IProfileIntelligenceService
{
    /// <summary>Build the "Hvad ved vi om &lt;app&gt;" picture for a profile in the current foreground context.</summary>
    IntelligencePicture Build(string profileName, string? foregroundExe);
}
