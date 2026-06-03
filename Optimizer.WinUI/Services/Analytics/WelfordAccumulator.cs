namespace Optimizer.WinUI.Services.Analytics;

/// <summary>
/// Online mean/variance via Welford's algorithm. Lets the anomaly baseline update
/// incrementally from one sample at a time without storing the full history.
/// </summary>
public readonly struct WelfordAccumulator(long count, double mean, double m2)
{
    public long Count { get; } = count;
    public double Mean { get; } = mean;
    public double M2 { get; } = m2;

    /// <summary>Population standard deviation (0 until at least 2 samples).</summary>
    public double StdDev => Count < 2 ? 0 : Math.Sqrt(M2 / Count);

    /// <summary>Fold a new value in, returning the updated accumulator.</summary>
    public WelfordAccumulator Add(double value)
    {
        var newCount = Count + 1;
        var delta = value - Mean;
        var newMean = Mean + delta / newCount;
        var delta2 = value - newMean;
        var newM2 = M2 + delta * delta2;
        return new WelfordAccumulator(newCount, newMean, newM2);
    }

    /// <summary>How many standard deviations <paramref name="value"/> sits from the mean.</summary>
    public double SigmaOf(double value)
    {
        var sd = StdDev;
        return sd <= 0 ? 0 : Math.Abs(value - Mean) / sd;
    }
}
