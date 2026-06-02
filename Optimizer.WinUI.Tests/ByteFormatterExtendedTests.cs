using System;
using Optimizer.WinUI.Helpers;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Extended tests for ByteFormatter edge cases and boundary values.
/// </summary>
public class ByteFormatterExtendedTests
{
    // ── Format (bytes) ──────────────────────────────────────────────────

    [Theory]
    [InlineData(1023, "1023 B")]          // just below 1 KB boundary
    [InlineData(1024, "1.0 KB")]          // exact 1 KB boundary
    [InlineData(1025, "1.0 KB")]          // just above 1 KB boundary
    [InlineData(1_048_575, "1024.0 KB")] // just below 1 MB (rounds within KB band)
    [InlineData(1_048_576, "1.0 MB")]    // exact 1 MB boundary
    [InlineData(1_073_741_823, "1024.0 MB")] // just below 1 GB
    [InlineData(1_073_741_824, "1.0 GB")] // exact 1 GB boundary
    public void Format_BoundaryValues(long bytes, string expected)
    {
        Assert.Equal(expected, ByteFormatter.Format(bytes));
    }

    [Fact]
    public void Format_ZeroBytes_Returns_ZeroB()
    {
        Assert.Equal("0 B", ByteFormatter.Format(0));
    }

    [Fact]
    public void Format_LargeMultiGb_CorrectUnit()
    {
        // 10 GB
        Assert.Equal("10.0 GB", ByteFormatter.Format(10L * 1_073_741_824));
    }

    // ── FormatSpeed (bytes/sec) ──────────────────────────────────────────

    [Theory]
    [InlineData(0.0, "0 B/s")]
    [InlineData(999.0, "999 B/s")]         // just below KB boundary
    [InlineData(1023.9, "1023 B/s")]       // still in bytes range
    [InlineData(1024.0, "1.0 KB/s")]       // exact KB boundary
    [InlineData(1_048_576.0, "1.0 MB/s")] // exact MB boundary
    [InlineData(1_073_741_824.0, "1.0 GB/s")] // exact GB boundary
    public void FormatSpeed_BoundaryValues(double bytesPerSec, string expected)
    {
        Assert.Equal(expected, ByteFormatter.FormatSpeed(bytesPerSec));
    }

    [Fact]
    public void FormatSpeed_HalfMegabyte_CorrectUnit()
    {
        // 512 KB/s
        Assert.Equal("512.0 KB/s", ByteFormatter.FormatSpeed(512 * 1024.0));
    }

    [Fact]
    public void FormatSpeed_NegativeSpeed_StaysInBytesRange()
    {
        // Negative speed is unusual (shouldn't occur in practice) but shouldn't throw
        var result = ByteFormatter.FormatSpeed(-100.0);
        Assert.NotNull(result);
        Assert.Contains("B/s", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_And_FormatSpeed_UseInvariantCulture()
    {
        // Ensure decimal separator is always '.' regardless of locale
        var formatted = ByteFormatter.Format(1536);      // 1.5 KB
        Assert.Equal("1.5 KB", formatted);

        var speedFormatted = ByteFormatter.FormatSpeed(1536.0);
        Assert.Equal("1.5 KB/s", speedFormatted);
    }

    // ── TB range (falls into GB band) ─────────────────────────────────────────

    [Fact]
    public void Format_TbRange_StillDisplaysAsGb()
    {
        // 1 TB — formatter has no TB tier, uses GB band
        long oneTb = 1_099_511_627_776L;
        var result = ByteFormatter.Format(oneTb);
        Assert.Contains("GB", result, StringComparison.Ordinal);
        Assert.DoesNotContain("B/s", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_TwoTb_CorrectGbValue()
    {
        long twoTb = 2L * 1_099_511_627_776L;
        var result = ByteFormatter.Format(twoTb);
        Assert.Equal("2048.0 GB", result);
    }

    [Fact]
    public void FormatSpeed_TbRange_DisplaysAsGb()
    {
        double oneTbPerSec = 1_099_511_627_776.0;
        var result = ByteFormatter.FormatSpeed(oneTbPerSec);
        Assert.Contains("GB/s", result, StringComparison.Ordinal);
    }

    // ── Negative bytes ────────────────────────────────────────────────────────

    [Fact]
    public void Format_NegativeBytes_DoesNotThrow()
    {
        // Negative values are unusual but the formatter should handle them gracefully
        var result = ByteFormatter.Format(-1024);
        Assert.NotNull(result);
        Assert.Contains("B", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NegativeBytes_FallsToBytesBand()
    {
        // -1024 is below all positive thresholds — falls to the _ case: displays as bytes
        var result = ByteFormatter.Format(-1024);
        Assert.Equal("-1024 B", result);
    }
}
