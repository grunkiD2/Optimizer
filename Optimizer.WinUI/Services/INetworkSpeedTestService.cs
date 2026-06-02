namespace Optimizer.WinUI.Services;

public class SpeedTestResult
{
    public double DownloadMbps { get; set; }
    public double UploadMbps { get; set; }
    public double PingMs { get; set; }
    public double JitterMs { get; set; }
    public DateTime TestedAt { get; set; }
}

public interface INetworkSpeedTestService
{
    Task<double> MeasureDownloadMbpsAsync(IProgress<double>? progress = null);
    Task<double> MeasureUploadMbpsAsync(IProgress<double>? progress = null);
    Task<(double pingMs, double jitterMs)> MeasurePingAsync();
    Task<SpeedTestResult> RunFullTestAsync(IProgress<string>? phaseProgress = null);
}
