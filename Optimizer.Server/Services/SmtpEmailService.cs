using System.Net;
using System.Net.Mail;

namespace Optimizer.Server.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    public SmtpEmailService(IConfiguration config) => _config = config;

    public async Task SendMagicLinkAsync(string toEmail, string magicLink)
    {
        var host = _config["Smtp:Host"] ?? throw new InvalidOperationException("Smtp:Host not set");
        var port = int.Parse(_config["Smtp:Port"] ?? "587");
        var user = _config["Smtp:Username"] ?? throw new InvalidOperationException("Smtp:Username not set");
        var pass = _config["Smtp:Password"] ?? throw new InvalidOperationException("Smtp:Password not set");
        var from = _config["Smtp:FromEmail"] ?? user;
        var fromName = _config["Smtp:FromName"] ?? "Optimizer";

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(user, pass)
        };

        var msg = new MailMessage
        {
            From = new MailAddress(from, fromName),
            Subject = "Your Optimizer sign-in link",
            Body = $"Click here to sign in:\n\n{magicLink}\n\nThis link expires in 15 minutes.",
            IsBodyHtml = false
        };
        msg.To.Add(toEmail);
        await client.SendMailAsync(msg);
    }
}
