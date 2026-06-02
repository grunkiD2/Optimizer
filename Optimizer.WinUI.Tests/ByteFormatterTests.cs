using Optimizer.WinUI.Helpers;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ByteFormatterTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1_048_576, "1.0 MB")]
    [InlineData(1_073_741_824, "1.0 GB")]
    public void Format_ReturnsCorrectUnit(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.Format(bytes));
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(1024, "1.0 KB/s")]
    [InlineData(1_048_576, "1.0 MB/s")]
    public void FormatSpeed_ReturnsCorrectUnit(double bytesPerSec, string expected)
    {
        Assert.Equal(expected, ByteFormatter.FormatSpeed(bytesPerSec));
    }
}
