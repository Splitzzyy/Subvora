using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;
using SubVora.Application.Notifications;

namespace SubVora.Infrastructure.Notifications;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Config is validated here, not in the constructor - AuthService (and therefore
    // AuthController, for every one of its actions) depends on IEmailSender, so throwing at
    // construction time would break login/register/etc too whenever SMTP isn't configured yet,
    // not just the one code path that actually needs to send an email.
    public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = int.TryParse(_configuration["Smtp:Port"], out var parsedPort) ? parsedPort : 587;
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var fromAddress = _configuration["Smtp:FromAddress"]
            ?? throw new InvalidOperationException("Smtp:FromAddress is not configured.");

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cancellationToken);

        if (!string.IsNullOrEmpty(username))
        {
            await client.AuthenticateAsync(username, password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
