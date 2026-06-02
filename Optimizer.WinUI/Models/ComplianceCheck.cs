using CommunityToolkit.Mvvm.ComponentModel;

namespace Optimizer.WinUI.Models;

public enum ComplianceStatus { Pass, Fail, NotApplicable }

public partial class ComplianceCheck : ObservableObject
{
    [ObservableProperty] private string id = "";
    [ObservableProperty] private string controlId = "";   // e.g. "CIS-1.1.1"
    [ObservableProperty] private string framework = "";   // CIS, NIST, HIPAA, SOC2
    [ObservableProperty] private string title = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private ComplianceStatus status = ComplianceStatus.NotApplicable;
    [ObservableProperty] private string evidence = "";

    public string StatusColor => Status switch
    {
        ComplianceStatus.Pass           => "#10B981",
        ComplianceStatus.Fail           => "#EF4444",
        _                               => "#6B7280"
    };

    public string StatusLabel => Status switch
    {
        ComplianceStatus.Pass           => "Pass",
        ComplianceStatus.Fail           => "Fail",
        _                               => "N/A"
    };
}
