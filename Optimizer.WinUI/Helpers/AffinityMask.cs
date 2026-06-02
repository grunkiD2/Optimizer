namespace Optimizer.WinUI.Helpers;

/// <summary>
/// Pure static helpers for building and validating processor-affinity bitmasks.
/// All methods are deterministic and have no side-effects — safe to unit-test.
/// </summary>
public static class AffinityMask
{
    /// <summary>
    /// Build a bitmask from an explicit list of 0-based core indices.
    /// E.g. cores [0,1,2] → 0b0111 = 7.
    /// </summary>
    /// <exception cref="ArgumentNullException">cores is null.</exception>
    public static long FromCores(IEnumerable<int> cores, int logicalCount)
    {
        ArgumentNullException.ThrowIfNull(cores);
        long mask = 0;
        foreach (var c in cores)
        {
            if (c >= 0 && c < logicalCount)
                mask |= 1L << c;
        }
        return mask;
    }

    /// <summary>
    /// Decompose a bitmask into the list of 0-based core indices that are set.
    /// E.g. 0b101 → [0, 2].
    /// </summary>
    public static int[] ToCores(long mask)
    {
        var result = new List<int>();
        for (var i = 0; i < 64; i++)
        {
            if ((mask & (1L << i)) != 0)
                result.Add(i);
        }
        return [.. result];
    }

    /// <summary>
    /// Returns true when mask is a valid non-zero affinity for a CPU with
    /// <paramref name="logicalCount"/> logical processors (bits must fit within range).
    /// </summary>
    public static bool IsValid(long mask, int logicalCount)
    {
        if (mask == 0) return false;
        if (logicalCount <= 0 || logicalCount > 64) return false;
        // All set bits must fall within [0, logicalCount-1]
        var maxMask = (1L << logicalCount) - 1;
        return (mask & ~maxMask) == 0;
    }

    /// <summary>
    /// Returns a full-affinity mask for a CPU with the given logical processor count.
    /// </summary>
    public static long AllCores(int logicalCount)
        => logicalCount >= 64 ? long.MaxValue : (1L << logicalCount) - 1;
}
