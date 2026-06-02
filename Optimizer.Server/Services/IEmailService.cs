namespace Optimizer.Server.Services;

public interface IEmailService
{
    Task SendMagicLinkAsync(string toEmail, string magicLink);
}
