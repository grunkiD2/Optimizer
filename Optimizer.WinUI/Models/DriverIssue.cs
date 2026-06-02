namespace Optimizer.WinUI.Models;

public class DriverIssue
{
    public string DeviceName { get; set; } = "";
    public string DeviceClass { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public DateTime? DriverDate { get; set; }
    public string Status { get; set; } = "";
    public int ConfigManagerErrorCode { get; set; }
    public string ErrorMessage { get; set; } = "";
    public bool IsConflict { get; set; }
    public bool IsOutdated { get; set; }

    public string SeverityLabel => ConfigManagerErrorCode != 0 ? "Error" : (IsOutdated ? "Outdated" : "OK");

    public string SeverityColor => SeverityLabel switch
    {
        "Error"    => "#EF4444",
        "Outdated" => "#F59E0B",
        _          => "#22C55E"
    };

    public string DriverDateDisplay => DriverDate.HasValue
        ? DriverDate.Value.ToString("yyyy-MM-dd")
        : "";
}
