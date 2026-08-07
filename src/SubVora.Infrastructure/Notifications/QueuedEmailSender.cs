using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SubVora.Application.Notifications;

namespace SubVora.Infrastructure.Notifications;

/// <summary>
/// Hands an email to a background queue and returns immediately, so no request's duration or
/// status code depends on whether an email was sent.
///
/// That dependency is an account-enumeration oracle, not a performance nit. Awaiting SMTP inline
/// made "this address is registered" measurable two ways: forgot-password answered 200 in 15ms for
/// an unknown address and 500 in 4.2s for a known one when the mail server was unreachable, and
/// register took ten times longer for a duplicate than for a new account. Both endpoints go to
/// great lengths to return identical responses; the wait gave it away regardless.
/// </summary>
public class QueuedEmailSender : IEmailSender
{
    /// <summary>
    /// Bounded so a burst cannot grow the queue without limit. Dropping the newest is the right
    /// failure here: the alternative is blocking the caller, which is the very coupling this class
    /// exists to remove.
    /// </summary>
    private const int QueueCapacity = 1_000;

    private readonly Channel<QueuedEmail> _queue = Channel.CreateBounded<QueuedEmail>(
        new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    private readonly ILogger<QueuedEmailSender> _logger;

    public QueuedEmailSender(ILogger<QueuedEmailSender> logger)
    {
        _logger = logger;
    }

    public ChannelReader<QueuedEmail> Reader => _queue.Reader;

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (!_queue.Writer.TryWrite(new QueuedEmail(toEmail, subject, body)))
        {
            // Never thrown back at the caller - a failure to enqueue must look exactly like a
            // success from the outside, or it becomes the same oracle by another route.
            _logger.LogError("Outbound email queue is full; dropped a message to {Recipient}.", toEmail);
        }

        return Task.CompletedTask;
    }
}

public record QueuedEmail(string ToEmail, string Subject, string Body);
