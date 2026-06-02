namespace Optimizer.WinUI.Services;

public enum StressTestState { Idle, Running, Completed, Aborted }

public class StressTestStatus
{
    public StressTestState State    { get; set; } = StressTestState.Idle;
    public TimeSpan        Elapsed  { get; set; }
    public double CurrentTempC      { get; set; }
    public double MaxTempC          { get; set; }
    public double CurrentCpuLoad    { get; set; }
    public string Message           { get; set; } = "";
    public bool   AbortedByWatchdog { get; set; }
}

public interface IStressTestService
{
    StressTestStatus Status { get; }
    event Action? StatusChanged;

    Task RunCpuStressAsync(TimeSpan duration, int maxTempC, CancellationToken ct);
    void Stop();

    Task<bool> LaunchPrime95Async();
    Task<bool> LaunchCinebenchAsync();

    bool IsPrime95Installed    { get; }
    bool IsCinebenchInstalled  { get; }
}
