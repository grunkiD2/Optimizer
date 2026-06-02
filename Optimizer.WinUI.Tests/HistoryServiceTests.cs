using System;
using System.Linq;
using Optimizer.WinUI.Models;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

/// <summary>
/// Tests for HistoryService record/query/clear behavior.
/// NOTE: HistoryService writes to %LocalAppData%\Optimizer\change-history.json.
/// Tests don't call Load() to avoid reading/overwriting production state.
/// </summary>
public class HistoryServiceTests
{
    [Fact]
    public void Entries_StartsEmpty()
    {
        var svc = new HistoryService();
        Assert.Empty(svc.Entries);
    }

    [Fact]
    public void RecordApplied_AddsEntry()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-disable-telemetry", "Disable Telemetry", "Privacy", reversible: true);

        Assert.Single(svc.Entries);
        var entry = svc.Entries[0];
        Assert.Equal("opt-disable-telemetry", entry.OptimizationId);
        Assert.Equal("Disable Telemetry", entry.OptimizationTitle);
        Assert.Equal("Privacy", entry.Category);
        Assert.Equal(HistoryAction.Applied, entry.Action);
        Assert.True(entry.IsReversible);
    }

    [Fact]
    public void RecordOneTime_AddsEntry_WithResultText()
    {
        var svc = new HistoryService();
        svc.RecordOneTime("opt-clear-temp", "Clear Temp Files", "Cleanup", "Freed 512 MB");

        Assert.Single(svc.Entries);
        var entry = svc.Entries[0];
        Assert.Equal(HistoryAction.OneTime, entry.Action);
        Assert.Equal("Freed 512 MB", entry.ResultText);
        Assert.False(entry.IsReversible);
    }

    [Fact]
    public void RecordUndone_AddsUndoneEntry()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-power", "Power Mode", "System", reversible: true);
        svc.RecordUndone("opt-power", "Power Mode", "System");

        Assert.Equal(2, svc.Entries.Count);
        // Most recent first — the Undone entry is at index 0
        Assert.Equal(HistoryAction.Undone, svc.Entries[0].Action);
    }

    [Fact]
    public void RecordUndone_MarksMatchingAppliedEntriesAsUndone()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-foo", "Foo Opt", "Perf", reversible: true);
        svc.RecordUndone("opt-foo", "Foo Opt", "Perf");

        // The original Applied entry should now have IsUndone = true
        var applied = svc.Entries.FirstOrDefault(e => e.Action == HistoryAction.Applied);
        Assert.NotNull(applied);
        Assert.True(applied!.IsUndone);
    }

    [Fact]
    public void RecordApplied_MultipleEntries_NewestFirst()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-a", "A", "Cat", reversible: false);
        svc.RecordApplied("opt-b", "B", "Cat", reversible: false);

        // Newest entry should be at index 0
        Assert.Equal("opt-b", svc.Entries[0].OptimizationId);
        Assert.Equal("opt-a", svc.Entries[1].OptimizationId);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-a", "A", "Cat", false);
        svc.RecordOneTime("opt-b", "B", "Cat", "done");
        Assert.Equal(2, svc.Entries.Count);

        svc.Clear();

        Assert.Empty(svc.Entries);
    }

    [Fact]
    public void RecordApplied_TimestampIsRecent()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var svc = new HistoryService();
        svc.RecordApplied("opt-ts", "TS Test", "Test", false);
        var after = DateTime.UtcNow.AddSeconds(1);

        var ts = svc.Entries[0].TimestampUtc;
        Assert.True(ts >= before && ts <= after);
    }

    [Fact]
    public void RecordApplied_EachEntryHasUniqueId()
    {
        var svc = new HistoryService();
        svc.RecordApplied("opt-a", "A", "Cat", false);
        svc.RecordApplied("opt-b", "B", "Cat", false);

        var ids = svc.Entries.Select(e => e.Id).ToList();
        Assert.Distinct(ids);
    }
}
