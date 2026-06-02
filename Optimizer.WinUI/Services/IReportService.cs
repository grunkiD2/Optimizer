namespace Optimizer.WinUI.Services;

public enum ReportType   { SystemSnapshot, HealthReport, OptimizationLog, FullReport }
public enum ReportFormat { Text, Html, Json }

public class GeneratedReport
{
    public string Content  { get; set; } = "";
    public string Title    { get; set; } = "";
    public DateTime Generated { get; set; } = DateTime.Now;
    public ReportFormat Format { get; set; }
}

public interface IReportService
{
    Task<GeneratedReport> GenerateAsync(ReportType type, ReportFormat format);
    Task<string> SaveReportAsync(GeneratedReport report);
}
