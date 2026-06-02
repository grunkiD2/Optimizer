namespace Optimizer.WinUI.Services;

public interface ISystemRepairService
{
    Task<bool> LaunchMemoryTestAsync();
    Task<bool> LaunchChkdskAsync(string drive);
    Task<bool> RunSfcScanAsync(IProgress<string>? progress = null);
    Task<bool> RunDismRepairAsync(IProgress<string>? progress = null);
}
