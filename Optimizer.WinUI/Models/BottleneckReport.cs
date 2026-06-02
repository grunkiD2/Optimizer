using System.Globalization;

namespace Optimizer.WinUI.Models;

public class ProcessBottleneck
{
    public int Pid { get; set; }
    public string ProcessName { get; set; } = "";
    public string BottleneckType { get; set; } = "";
    public double Value { get; set; }
    public string ValueUnit { get; set; } = "";
    public string Severity { get; set; } = "Low";

    public string SeverityColor => Severity switch
    {
        "High"   => "#EF4444",
        "Medium" => "#F59E0B",
        _        => "#3B82F6"
    };

    public string DisplayValue => string.Create(CultureInfo.InvariantCulture, $"{Value:F1} {ValueUnit}");
}

public class BottleneckReport
{
    public List<ProcessBottleneck> TopOffenders { get; set; } = [];
    public string Summary { get; set; } = "";
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}
