namespace Optimizer.WinUI.Services.Ai;

/// <summary>
/// Laplace-mechanism differential privacy for numeric aggregates.
/// Used to privatize model statistics before they leave the device.
///
/// The Laplace mechanism guarantees ε-differential privacy: by adding
/// noise calibrated to sensitivity/epsilon, an observer cannot determine
/// from the output whether any individual data point was included.
/// Lower epsilon = more privacy = more noise injected.
/// </summary>
public static class DifferentialPrivacy
{
    /// <summary>
    /// Adds Laplace noise calibrated to sensitivity/epsilon to a value.
    /// Lower epsilon = more privacy (more noise). Higher epsilon = less noise.
    ///
    /// Laplace mechanism: noise = -scale * sign(u) * ln(1 - 2|u|)
    /// where u ~ Uniform(-0.5, 0.5) and scale = sensitivity / epsilon.
    /// </summary>
    /// <param name="value">The true value to perturb.</param>
    /// <param name="sensitivity">The global sensitivity of the function (max change caused by one record).</param>
    /// <param name="epsilon">Privacy budget. Must be > 0. Typical values: 0.1 (strong) to 10 (weak).</param>
    /// <param name="rng">Random source. Pass a seeded Random in tests for determinism.</param>
    public static double AddLaplaceNoise(double value, double sensitivity, double epsilon, Random rng)
    {
        if (epsilon <= 0) throw new ArgumentOutOfRangeException(nameof(epsilon), "epsilon must be > 0");
        if (sensitivity <= 0) throw new ArgumentOutOfRangeException(nameof(sensitivity), "sensitivity must be > 0");

        double scale = sensitivity / epsilon;

        // Sample u from Uniform(-0.5, 0.5) avoiding the exact boundary values
        double u;
        do { u = rng.NextDouble() - 0.5; } while (u == 0.0 || Math.Abs(u) >= 0.5);

        double noise = -scale * Math.Sign(u) * Math.Log(1.0 - 2.0 * Math.Abs(u));
        return value + noise;
    }

    /// <summary>
    /// Clamps and noise-perturbs a count, ensuring the result is never negative.
    /// Sensitivity = 1.0 by default (adding/removing one record changes the count by at most 1).
    /// </summary>
    public static double PrivatizeCount(double count, double epsilon, Random rng, double sensitivity = 1.0)
    {
        double noised = AddLaplaceNoise(count, sensitivity, epsilon, rng);
        return Math.Max(0.0, noised);  // counts cannot be negative
    }

    /// <summary>
    /// Privatizes a rate in [0, 1] (e.g. acceptance rate) with bounded Laplace noise.
    /// Sensitivity = 1.0 (adding one user can shift a rate by at most 1 in the worst case).
    /// Result is clamped to [0, 1].
    /// </summary>
    public static double PrivatizeRate(double rate, double epsilon, Random rng)
    {
        double noised = AddLaplaceNoise(rate, sensitivity: 1.0, epsilon, rng);
        return Math.Clamp(noised, 0.0, 1.0);
    }
}
