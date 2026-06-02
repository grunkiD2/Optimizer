namespace Optimizer.WinUI.Models;

public class NetworkDiagnostic
{
    public string Type { get; set; } = "";
    public string Target { get; set; } = "";
    public string Result { get; set; } = "";
    public bool Success { get; set; }

    public string StatusIcon => Success ? "✅" : "❌";
    public string StatusColor => Success ? "#22C55E" : "#EF4444";
}
