using Microsoft.Win32;

namespace Optimizer.WinUI.Services;

public class PrivacyService : IPrivacyService
{
    private record SettingDef(
        string Id,
        string Name,
        string Description,
        RegistryHive Hive,
        string Path,
        string Value,
        object PrivacyValue,
        object OffValue,
        RegistryValueKind Kind = RegistryValueKind.DWord);

    private readonly List<SettingDef> _defs =
    [
        new("ads-id",
            "Advertising ID",
            "Apps can use your advertising ID for personalized ads",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
            "Enabled", 0, 1),

        new("recall",
            "Windows Recall",
            "AI feature that records and indexes your screen activity",
            RegistryHive.CurrentUser,
            @"Software\Policies\Microsoft\Windows\WindowsAI",
            "DisableAIDataAnalysis", 1, 0),

        new("copilot",
            "Copilot",
            "AI assistant in the taskbar",
            RegistryHive.CurrentUser,
            @"Software\Policies\Microsoft\Windows\WindowsCopilot",
            "TurnOffWindowsCopilot", 1, 0),

        new("widgets",
            "News & Widgets",
            "Widget panel and news feed in the taskbar",
            RegistryHive.LocalMachine,
            @"Software\Policies\Microsoft\Dsh",
            "AllowNewsAndInterests", 0, 1),

        new("location",
            "Location Services",
            "System-wide location access for apps",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location",
            "Value", "Deny", "Allow", RegistryValueKind.String),

        new("camera",
            "Camera Access",
            "App access to your camera",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam",
            "Value", "Deny", "Allow", RegistryValueKind.String),

        new("microphone",
            "Microphone Access",
            "App access to your microphone",
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone",
            "Value", "Deny", "Allow", RegistryValueKind.String),

        new("activity-history",
            "Activity History",
            "Sends activity history to Microsoft",
            RegistryHive.LocalMachine,
            @"Software\Policies\Microsoft\Windows\System",
            "PublishUserActivities", 0, 1),

        new("tailored-experiences",
            "Tailored Experiences",
            "Personalized tips and ads based on diagnostic data",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\Privacy",
            "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1),

        new("inking-typing",
            "Inking & Typing",
            "Improves typing experience by sending data to Microsoft",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\InputPersonalization",
            "RestrictImplicitTextCollection", 1, 0),

        new("feedback-frequency",
            "Feedback Requests",
            "Frequency of Windows feedback dialogs",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Siuf\Rules",
            "NumberOfSIUFInPeriod", 0, 1),

        new("online-speech",
            "Online Speech Recognition",
            "Sends speech data to the cloud for processing",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy",
            "HasAccepted", 0, 1),

        new("diagnostic-level",
            "Diagnostic Data",
            "Limits diagnostic data to required only (not optional)",
            RegistryHive.LocalMachine,
            @"Software\Microsoft\Windows\CurrentVersion\Policies\DataCollection",
            "AllowTelemetry", 1, 3),

        new("start-suggestions",
            "Start Menu Suggestions",
            "App advertisements and suggestions in the Start menu",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            "SubscribedContent-338388Enabled", 0, 1),

        new("lockscreen-ads",
            "Lock Screen Ads",
            "Spotlight advertisements on the lock screen",
            RegistryHive.CurrentUser,
            @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            "RotatingLockScreenOverlayEnabled", 0, 1),
    ];

    public Task<IReadOnlyList<PrivacySetting>> GetAllAsync()
    {
        var result = _defs.Select(d => new PrivacySetting
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            IsPrivacyFriendly = ValuesEqual(ReadCurrentValue(d), d.PrivacyValue)
        }).ToList();

        return Task.FromResult<IReadOnlyList<PrivacySetting>>(result);
    }

    public Task<bool> SetEnabledAsync(string id, bool enableForPrivacy)
    {
        var def = _defs.FirstOrDefault(d => d.Id == id);
        if (def == null) return Task.FromResult(false);

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(def.Hive, RegistryView.Registry64);
            using var subKey = baseKey.CreateSubKey(def.Path, writable: true);
            if (subKey == null) return Task.FromResult(false);
            subKey.SetValue(def.Value, enableForPrivacy ? def.PrivacyValue : def.OffValue, def.Kind);
            return Task.FromResult(true);
        }
        catch { return Task.FromResult(false); }
    }

    private object? ReadCurrentValue(SettingDef def)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(def.Hive, RegistryView.Registry64);
            using var subKey = baseKey.OpenSubKey(def.Path);
            return subKey?.GetValue(def.Value);
        }
        catch { return null; }
    }

    private static bool ValuesEqual(object? current, object expected)
    {
        if (current == null) return false;
        if (expected is string s) return string.Equals(current.ToString(), s, StringComparison.OrdinalIgnoreCase);
        if (expected is int i && current is int ci) return ci == i;
        // Registry may return int for DWORD
        if (expected is int ei)
        {
            if (int.TryParse(current.ToString(), out var parsed)) return parsed == ei;
        }
        return Equals(current, expected);
    }
}
