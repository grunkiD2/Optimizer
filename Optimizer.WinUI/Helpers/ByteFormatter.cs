using System.Globalization;

namespace Optimizer.WinUI.Helpers;

public static class ByteFormatter
{
    private const long Gb = 1_073_741_824;
    private const long Mb = 1_048_576;
    private const long Kb = 1_024;

    public static string Format(long bytes) => bytes switch
    {
        >= Gb => ((double)bytes / Gb).ToString("F1", CultureInfo.InvariantCulture) + " GB",
        >= Mb => ((double)bytes / Mb).ToString("F1", CultureInfo.InvariantCulture) + " MB",
        >= Kb => ((double)bytes / Kb).ToString("F1", CultureInfo.InvariantCulture) + " KB",
        _ => $"{bytes} B"
    };

    public static string FormatSpeed(double bytesPerSecond) => bytesPerSecond switch
    {
        >= Gb => (bytesPerSecond / Gb).ToString("F1", CultureInfo.InvariantCulture) + " GB/s",
        >= Mb => (bytesPerSecond / Mb).ToString("F1", CultureInfo.InvariantCulture) + " MB/s",
        >= Kb => (bytesPerSecond / Kb).ToString("F1", CultureInfo.InvariantCulture) + " KB/s",
        _ => ((long)bytesPerSecond) + " B/s"
    };
}
