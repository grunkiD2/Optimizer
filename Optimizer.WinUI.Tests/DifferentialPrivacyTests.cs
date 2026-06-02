using System;
using System.Linq;
using Optimizer.WinUI.Services.Ai;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for the Laplace differential-privacy mechanism.
/// All tests use seeded Random instances for determinism.
/// </summary>
public class DifferentialPrivacyTests
{
    // ── AddLaplaceNoise ──────────────────────────────────────────────────────

    [Fact]
    public void AddLaplaceNoise_WithFixedSeed_IsDeterministic()
    {
        var rng1 = new Random(42);
        var rng2 = new Random(42);

        double r1 = DifferentialPrivacy.AddLaplaceNoise(50.0, sensitivity: 1.0, epsilon: 1.0, rng1);
        double r2 = DifferentialPrivacy.AddLaplaceNoise(50.0, sensitivity: 1.0, epsilon: 1.0, rng2);

        Assert.Equal(r1, r2);
    }

    [Fact]
    public void AddLaplaceNoise_IsUnbiased_OverManySamples()
    {
        // With many samples the mean should converge close to the true value (unbiasedness).
        const int n = 10_000;
        const double trueValue = 75.0;
        const double epsilon   = 1.0;
        const double tolerance = 1.5; // generous tolerance for statistical test

        var rng = new Random(12345);
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += DifferentialPrivacy.AddLaplaceNoise(trueValue, sensitivity: 1.0, epsilon, rng);

        double mean = sum / n;
        Assert.True(Math.Abs(mean - trueValue) < tolerance,
            $"Mean {mean:F4} deviated more than {tolerance} from true value {trueValue}");
    }

    [Fact]
    public void AddLaplaceNoise_SmallerEpsilon_ProducesLargerVariance()
    {
        // Lower epsilon = more noise = higher variance.
        const int n = 5_000;
        const double value = 50.0;

        double VarAt(double eps)
        {
            var rng = new Random(999);
            double[] samples = Enumerable.Range(0, n)
                .Select(_ => DifferentialPrivacy.AddLaplaceNoise(value, 1.0, eps, rng))
                .ToArray();
            double mean = samples.Average();
            return samples.Average(s => (s - mean) * (s - mean));
        }

        double varHighPrivacy = VarAt(0.1); // strong privacy → much noise
        double varLowPrivacy  = VarAt(10.0); // weak privacy → little noise

        Assert.True(varHighPrivacy > varLowPrivacy,
            $"Expected variance at eps=0.1 ({varHighPrivacy:F2}) > variance at eps=10 ({varLowPrivacy:F2})");
    }

    [Fact]
    public void AddLaplaceNoise_ThrowsOnNonPositiveEpsilon()
    {
        var rng = new Random(1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DifferentialPrivacy.AddLaplaceNoise(10.0, 1.0, epsilon: 0, rng));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DifferentialPrivacy.AddLaplaceNoise(10.0, 1.0, epsilon: -1, rng));
    }

    // ── PrivatizeCount ───────────────────────────────────────────────────────

    [Fact]
    public void PrivatizeCount_NeverReturnsNegative()
    {
        var rng = new Random(77);
        // Run many iterations with a count of 0 (most likely to produce negative noise)
        for (int i = 0; i < 1_000; i++)
        {
            double result = DifferentialPrivacy.PrivatizeCount(0.0, epsilon: 0.5, rng);
            Assert.True(result >= 0.0, $"Expected ≥ 0 but got {result}");
        }
    }

    [Fact]
    public void PrivatizeCount_PositiveCount_NeverReturnsNegative()
    {
        var rng = new Random(88);
        for (int i = 0; i < 1_000; i++)
        {
            double result = DifferentialPrivacy.PrivatizeCount(5.0, epsilon: 0.1, rng);
            Assert.True(result >= 0.0, $"Expected ≥ 0 but got {result}");
        }
    }

    // ── PrivatizeRate ────────────────────────────────────────────────────────

    [Fact]
    public void PrivatizeRate_StaysInZeroOne()
    {
        var rng = new Random(55);
        double[] rates = [0.0, 0.1, 0.5, 0.9, 1.0];
        foreach (var rate in rates)
        {
            for (int i = 0; i < 500; i++)
            {
                double result = DifferentialPrivacy.PrivatizeRate(rate, epsilon: 0.5, rng);
                Assert.True(result >= 0.0 && result <= 1.0,
                    $"Rate {result} is outside [0,1] for input rate {rate}");
            }
        }
    }

    [Fact]
    public void PrivatizeRate_WithHighEpsilon_StaysCloseToTrueValue()
    {
        // With epsilon=100 noise is tiny; averaged over many samples should be near original.
        const int n      = 5_000;
        const double rate = 0.7;
        const double eps  = 100.0;
        const double tol  = 0.05;

        var rng = new Random(321);
        double mean = Enumerable.Range(0, n)
            .Select(_ => DifferentialPrivacy.PrivatizeRate(rate, eps, rng))
            .Average();

        Assert.True(Math.Abs(mean - rate) < tol,
            $"Mean privatized rate {mean:F4} deviated from {rate} by more than {tol}");
    }
}
