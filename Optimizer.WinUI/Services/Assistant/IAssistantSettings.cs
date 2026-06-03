namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Read access to the assistant-related user settings.</summary>
public interface IAssistantSettings
{
    bool AllowActions { get; }
    string Model { get; }
}
