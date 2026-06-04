namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Read access to the assistant-related user settings.</summary>
public interface IAssistantSettings
{
    bool AllowActions { get; }
    string Model { get; }

    /// <summary>When the app is running elevated, the assistant may skip per-tool-call
    /// confirmation prompts. The user already granted admin rights to the whole process.</summary>
    bool AutoConfirmWhenElevated { get; }
}
