namespace Optimizer.Server.Services;

public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> _log;
    public ConsoleEmailService(ILogger<ConsoleEmailService> log) => _log = log;

    public Task SendMagicLinkAsync(string toEmail, string magicLink)
    {
        _log.LogInformation("MAGIC LINK for {Email}:\n  {Link}\n", toEmail, magicLink);
        Console.WriteLine($"\nMAGIC LINK for {toEmail}:\n  {magicLink}\n");
        return Task.CompletedTask;
    }
}
