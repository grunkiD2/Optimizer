namespace Optimizer.WinUI.Services.Power;

public enum DriftClass { Learning, Normal, Elevated, Anomalous }

/// <summary>Per-(context, process) online baseline — Welford μ/M₂ with exponential forgetting.</summary>
public class PowerBaseline
{
    public double Count { get; set; }
    public double MeanW { get; set; }
    public double M2 { get; set; }
    public double EwmaW { get; set; }
    public DateTimeOffset LastUpdated { get; set; }

    public double SigmaW => Count > 1 ? Math.Sqrt(M2 / Count) : 0;
}

/// <summary>
/// Adaptive Drift Surfacing math (docs/POWER-INSIGHTS.md §3): Welford online statistics with
/// half-life decay (a process whose normal profile shifts re-baselines smoothly) and a robust
/// modified-z classification with a sigma floor (steady processes have σ≈0 — a hard z would
/// scream on +0.1 W). Pure logic, no I/O.
/// </summary>
public static class DriftDetector
{
    public const double MinSamplesBeforeClassify = 20;
    public const double SigmaFloorW = 0.75;
    public const double ElevatedFraction = 0.6; // elevated at 60% of the anomalous threshold

    /// <summary>Classify BEFORE updating the baseline with the observation.</summary>
    public static DriftClass Classify(PowerBaseline baseline, double observedW, double zThreshold)
    {
        if (baseline.Count < MinSamplesBeforeClassify) return DriftClass.Learning;
        var sigma = Math.Max(baseline.SigmaW, SigmaFloorW);
        var z = (observedW - baseline.MeanW) / sigma; // one-sided: only EXCESS power drifts
        if (z >= zThreshold) return DriftClass.Anomalous;
        if (z >= zThreshold * ElevatedFraction) return DriftClass.Elevated;
        return DriftClass.Normal;
    }

    public static double ZScore(PowerBaseline baseline, double observedW)
        => (observedW - baseline.MeanW) / Math.Max(baseline.SigmaW, SigmaFloorW);

    /// <summary>Decay-then-update: effective sample size and spread decay with the half-life,
    /// then a standard Welford step folds in the new observation.</summary>
    public static void Update(PowerBaseline baseline, double observedW, DateTimeOffset now, double halfLifeHours)
    {
        if (baseline.Count > 0 && baseline.LastUpdated != default && halfLifeHours > 0)
        {
            var hours = (now - baseline.LastUpdated).TotalHours;
            if (hours > 0)
            {
                var factor = Math.Pow(0.5, hours / halfLifeHours);
                baseline.Count *= factor;
                baseline.M2 *= factor;
            }
        }

        baseline.Count += 1;
        var delta = observedW - baseline.MeanW;
        baseline.MeanW += delta / baseline.Count;
        baseline.M2 += delta * (observedW - baseline.MeanW);
        baseline.EwmaW = baseline.Count <= 1 ? observedW : 0.2 * observedW + 0.8 * baseline.EwmaW;
        baseline.LastUpdated = now;
    }
}
