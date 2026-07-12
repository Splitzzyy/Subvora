using System.Collections.Concurrent;
using SubVora.Application.Notifications;

namespace SubVora.Api.Tests;

/// <summary>Deterministic stand-in for SmtpEmailSender - Api.Tests never dial out to a real SMTP server.</summary>
public class FakeEmailSender : IEmailSender
{
    public record SentEmail(string ToEmail, string Subject, string Body);

    public ConcurrentBag<SentEmail> SentEmails { get; } = new();

    public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
    {
        SentEmails.Add(new SentEmail(toEmail, subject, body));
        return Task.CompletedTask;
    }
}
