using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SubVora.Infrastructure.Notifications;

/// <summary>
/// Drains the queue <see cref="QueuedEmailSender"/> writes to and actually talks to SMTP, off the
/// request path. A send failure is logged and dropped: password reset codes and
/// already-registered notices are both re-requestable, and retrying here would need durable
/// storage this app does not have.
/// </summary>
public class EmailDispatchBackgroundService : BackgroundService
{
    private readonly QueuedEmailSender _queue;
    private readonly SmtpEmailSender _smtpEmailSender;
    private readonly ILogger<EmailDispatchBackgroundService> _logger;

    public EmailDispatchBackgroundService(
        QueuedEmailSender queue,
        SmtpEmailSender smtpEmailSender,
        ILogger<EmailDispatchBackgroundService> logger)
    {
        _queue = queue;
        _smtpEmailSender = smtpEmailSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Said once, at boot, rather than left to be inferred from the absence of mail. Nothing
        // here throws: email is not required to run the API, and refusing to start over it would
        // take down subscription tracking to protect password resets.
        if (_smtpEmailSender.MissingRequiredSettings is { Count: > 0 } missing)
        {
            _logger.LogWarning(
                "SMTP is not configured ({MissingSettings} unset) - no email will be delivered. See docs/DEPLOYMENT.md.",
                string.Join(", ", missing));
        }

        await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _smtpEmailSender.SendAsync(email.ToEmail, email.Subject, email.Body, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Must not take the host down, and must not surface anywhere the caller can see.
                _logger.LogWarning(ex, "Could not deliver an email to {Recipient}.", email.ToEmail);
            }
        }
    }
}
