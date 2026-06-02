namespace Optimizer.WinUI.Services.Cloud;

public interface ICloudSyncOrchestrator
{
    bool IsEnabled { get; }
    DateTime? LastSyncAtUtc { get; }
    string? LastError { get; }
    long CurrentCursor { get; }

    Task<bool> SyncNowAsync();
    Task EnableAsync();
    Task DisableAsync();
}
