namespace Optimizer.WinUI.Services;

public interface IApiHostService
{
    Task StartAsync(int port, string token);
    Task StopAsync();
    bool IsRunning { get; }
    string ListeningUrl { get; }
}
