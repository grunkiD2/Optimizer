using Optimizer.WinUI.Services;

namespace Optimizer.WinUI.Services.Assistant;

/// <summary>Reads assistant settings from the app settings service.</summary>
public sealed class AssistantSettings(ISettingsService settings) : IAssistantSettings
{
    public bool AllowActions => settings.Settings.AssistantAllowActions;
    public string Model => string.IsNullOrWhiteSpace(settings.Settings.AssistantModel)
        ? "claude-sonnet-4-6"
        : settings.Settings.AssistantModel;
}
