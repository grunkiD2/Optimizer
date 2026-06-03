using System;
using Optimizer.WinUI.Services;
using Xunit;

namespace Optimizer.WinUI.Tests;

public class ScheduledOptimizationTests
{
    [Fact]
    public void IntervalMinutes_adds_the_interval()
    {
        var task = new ScheduledTask { ScheduleType = "IntervalMinutes", ScheduleValue = "30" };
        var from = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc);

        var next = ScheduledOptimizationService.ComputeNextRun(task, from);

        Assert.Equal(from.AddMinutes(30), next);
    }

    [Fact]
    public void Once_returns_the_time_when_in_the_future_and_null_when_past()
    {
        var future = DateTime.UtcNow.AddHours(2);
        var taskFuture = new ScheduledTask { ScheduleType = "Once", ScheduleValue = future.ToString("O") };
        Assert.NotNull(ScheduledOptimizationService.ComputeNextRun(taskFuture, DateTime.UtcNow));

        var past = DateTime.UtcNow.AddHours(-2);
        var taskPast = new ScheduledTask { ScheduleType = "Once", ScheduleValue = past.ToString("O") };
        Assert.Null(ScheduledOptimizationService.ComputeNextRun(taskPast, DateTime.UtcNow));
    }

    [Fact]
    public void DailyAt_is_always_in_the_future()
    {
        var task = new ScheduledTask { ScheduleType = "DailyAt", ScheduleValue = "03:00" };
        var next = ScheduledOptimizationService.ComputeNextRun(task, DateTime.UtcNow);

        Assert.NotNull(next);
        Assert.True(next > DateTime.UtcNow);
        // The next occurrence is within the next 24 hours.
        Assert.True(next <= DateTime.UtcNow.AddDays(1).AddMinutes(1));
    }

    [Fact]
    public void Invalid_values_return_null()
    {
        Assert.Null(ScheduledOptimizationService.ComputeNextRun(
            new ScheduledTask { ScheduleType = "IntervalMinutes", ScheduleValue = "abc" }, DateTime.UtcNow));
        Assert.Null(ScheduledOptimizationService.ComputeNextRun(
            new ScheduledTask { ScheduleType = "DailyAt", ScheduleValue = "not-a-time" }, DateTime.UtcNow));
        Assert.Null(ScheduledOptimizationService.ComputeNextRun(
            new ScheduledTask { ScheduleType = "Unknown", ScheduleValue = "x" }, DateTime.UtcNow));
    }
}
