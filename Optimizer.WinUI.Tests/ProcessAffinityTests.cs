using System;
using System.Diagnostics;
using System.Linq;
using Optimizer.WinUI.Helpers;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for <see cref="AffinityMask"/> helper methods (pure logic, no process spawning)
/// and one integration test that sets/restores affinity on the current process.
/// </summary>
public class ProcessAffinityTests
{
    // ── AffinityMask.FromCores ────────────────────────────────────────────────

    [Fact]
    public void FromCores_FirstThreeCores_ReturnsBitmask0b111()
    {
        var mask = AffinityMask.FromCores([0, 1, 2], logicalCount: 8);
        Assert.Equal(0b0111L, mask);
    }

    [Fact]
    public void FromCores_OddCores_ReturnsBitmask()
    {
        var mask = AffinityMask.FromCores([0, 2, 4], logicalCount: 8);
        Assert.Equal(0b010101L, mask);
    }

    [Fact]
    public void FromCores_OutOfRangeCoresIgnored()
    {
        // Core 8 is out of range for a logicalCount=8 CPU
        var mask = AffinityMask.FromCores([0, 8], logicalCount: 8);
        Assert.Equal(1L, mask);  // only core 0
    }

    // ── AffinityMask.ToCores ──────────────────────────────────────────────────

    [Fact]
    public void ToCores_Mask0b101_ReturnsCores0And2()
    {
        var cores = AffinityMask.ToCores(0b101L);
        Assert.Equal([0, 2], cores);
    }

    [Fact]
    public void ToCores_AllCoresMask_ReturnsAllIndices()
    {
        var mask = AffinityMask.AllCores(4);  // 0b1111 = 15
        var cores = AffinityMask.ToCores(mask);
        Assert.Equal([0, 1, 2, 3], cores);
    }

    // ── AffinityMask.IsValid ──────────────────────────────────────────────────

    [Fact]
    public void IsValid_ZeroMask_ReturnsFalse()
    {
        Assert.False(AffinityMask.IsValid(0L, 8));
    }

    [Fact]
    public void IsValid_MaskExceedingCoreCount_ReturnsFalse()
    {
        // Bit 8 set on a 8-core CPU is out of range
        Assert.False(AffinityMask.IsValid(1L << 8, logicalCount: 8));
    }

    [Fact]
    public void IsValid_ValidSingleCoreMask_ReturnsTrue()
    {
        Assert.True(AffinityMask.IsValid(1L, logicalCount: 4));
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_FromCoresToCores_IsIdentity()
    {
        int[] original = [0, 2, 4, 6];
        var mask  = AffinityMask.FromCores(original, logicalCount: 8);
        var back  = AffinityMask.ToCores(mask);
        Assert.Equal(original, back);
    }

    // ── Integration: set affinity on current process then restore ─────────────

    [Fact]
    public void Integration_SetAffinityOnCurrentProcess_ReadBackAndRestore()
    {
        // This test modifies the current process's affinity for a moment.
        // It restores the original mask in the finally block.
        using var self = Process.GetCurrentProcess();
        var originalMask = self.ProcessorAffinity.ToInt64();

        try
        {
            // Pin to core 0 only
            self.ProcessorAffinity = (IntPtr)1L;
            var readBack = self.ProcessorAffinity.ToInt64();
            Assert.Equal(1L, readBack);
        }
        finally
        {
            // Always restore
            self.ProcessorAffinity = (IntPtr)originalMask;
        }
    }
}
