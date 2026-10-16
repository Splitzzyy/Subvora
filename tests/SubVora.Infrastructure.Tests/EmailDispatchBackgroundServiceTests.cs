using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SubVora.Infrastructure.Notifications;

namespace SubVora.Infrastructure.Tests;

public class EmailDispatchBackgroundServiceTests
{
    private const string Recipient = "victim@example.com";

    [Fact]
    public async Task ADeliveryFailure_IsLoggedWithoutTheAddress()
    {
        // Every send fails while SMTP is unconfigured or Brevo is down, so this line is written for
        // every email - and with the address in it, every user's address reached the log sink.
        var entry = await DispatchOneFailingAsync(new InvalidOperationException("Smtp:Host is not configured."));

        Assert.Contains("Your SubVora password reset code", entry);
        Assert.DoesNotContain(Recipient, entry);
    }

    [Fact]
    public async Task ARejectedRecipient_IsLoggedWithoutTheServersReply()
    {
        // The server's reply quotes the mailbox it refused, so logging the exception would put the
        // address straight back.
        var rejection = new SmtpCommandException(
            SmtpErrorCode.RecipientNotAccepted,
            SmtpStatusCode.MailboxUnavailable,
            new MimeKit.MailboxAddress(null, Recipient),
            $"5.1.1 <{Recipient}>: Recipient address rejected");

        var entry = await DispatchOneFailingAsync(rejection);

        Assert.Contains("RecipientNotAccepted", entry);
        Assert.DoesNotContain(Recipient, entry);
    }

    private static async Task<string> DispatchOneFailingAsync(Exception failure)
    {
        var queue = new QueuedEmailSender(NullLogger<QueuedEmailSender>.Instance);
        var logger = new CapturingLogger<EmailDispatchBackgroundService>();
        var service = new EmailDispatchBackgroundService(queue, new FailingSmtpEmailSender(failure), logger);

        await queue.SendAsync(Recipient, "Your SubVora password reset code", "Your password reset code is 123456.");
        await service.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (logger.Entries.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        return Assert.Single(logger.Entries);
    }

    private sealed class FailingSmtpEmailSender(Exception failure)
        : SmtpEmailSender(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Smtp:Host"] = "localhost", ["Smtp:FromAddress"] = "from@example.com" })
            .Build())
    {
        public override Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default) =>
            Task.FromException(failure);
    }
}
