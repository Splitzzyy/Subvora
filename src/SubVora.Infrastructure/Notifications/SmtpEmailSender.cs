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

    /// <summary>
    /// The settings that must be present for any send to succeed, and are not, or an empty list.
    /// Read at startup by <see cref="EmailDispatchBackgroundService"/> so an unconfigured mailer
    /// says so once on boot instead of only when the first user waits for an email that is never
    /// coming - the failure is otherwise invisible, since every send is fire-and-forget by design.
    /// </summary>
    public IReadOnlyList<string> MissingRequiredSettings =>
        new[] { "Smtp:Host", "Smtp:FromAddress" }
            .Where(key => string.IsNullOrWhiteSpace(_configuration[key]))
            .ToList();

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

        // Required by default: a real mail server carries reset codes, and StartTlsWhenAvailable
        // would let anyone able to strip the server's STARTTLS advertisement read them in transit.
        // Local mail catchers (Mailpit, MailHog) offer no TLS at all, which is why every local send
        // failed with "The SMTP server does not support the STARTTLS extension" - opt out there.
        var useStartTls = !bool.TryParse(_configuration["Smtp:UseStartTls"], out var parsed) || parsed;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);

        if (!string.IsNullOrEmpty(username))
        {
            await client.AuthenticateAsync(username, password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
