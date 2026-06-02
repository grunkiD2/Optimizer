namespace Optimizer.WinUI.Helpers;

public static class ByteFormatter
{
    private const long Gb = 1_073_741_824;
    private const long Mb = 1_048_576;
    private const long Kb = 1_024;

    public static string Format(long bytes) => bytes switch
    {
        >= Gb => $"{bytes / (double)Gb:F1} GB",
        >= Mb => $"{bytes / (double)Mb:F1} MB",
        >= Kb => $"{bytes / (double)Kb:F1} KB",
        _ => $"{bytes} B"
    };

    public static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= Gb => $"{bytesPerSecond / Gb:F1} GB/s",
        >= Mb => $"{bytesPerSecond / Mb:F1} MB/s",
        >= Kb => $"{bytesPerSecond / Kb:F1} KB/s",
        _ => $"{bytesPerSecond:F0} B/s"
    };
}
