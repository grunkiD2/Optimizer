using System;
using Optimizer.WinUI.Services.Analytics;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class WelfordAccumulatorTests
{
    [Fact]
    public void Mean_matches_simple_average()
    {
        var acc = new WelfordAccumulator(0, 0, 0);
        foreach (var v in new[] { 2.0, 4.0, 6.0, 8.0 })
            acc = acc.Add(v);

        Assert.Equal(4, acc.Count);
        Assert.Equal(5.0, acc.Mean, 6);
    }

    [Fact]
    public void StdDev_is_zero_for_constant_stream_and_positive_for_varied()
    {
        var constant = new WelfordAccumulator(0, 0, 0);
        for (var i = 0; i < 10; i++) constant = constant.Add(50.0);
        Assert.Equal(0, constant.StdDev, 6);

        var varied = new WelfordAccumulator(0, 0, 0);
        foreach (var v in new[] { 10.0, 20.0, 30.0, 40.0, 50.0 })
            varied = varied.Add(v);
        Assert.True(varied.StdDev > 0);
    }

    [Fact]
    public void SigmaOf_flags_an_outlier()
    {
        var acc = new WelfordAccumulator(0, 0, 0);
        // Tight cluster around 50.
        foreach (var v in new[] { 48.0, 49.0, 50.0, 51.0, 52.0, 50.0, 49.0, 51.0 })
            acc = acc.Add(v);

        // A reading of 100 should be many sigma away.
        Assert.True(acc.SigmaOf(100.0) >= 2.0);
        // A reading near the mean should be well within 2 sigma.
        Assert.True(acc.SigmaOf(50.0) < 2.0);
    }

    [Fact]
    public void SigmaOf_is_zero_without_variance()
    {
        var acc = new WelfordAccumulator(0, 0, 0).Add(5).Add(5);
        Assert.Equal(0, acc.SigmaOf(999));
    }
}
