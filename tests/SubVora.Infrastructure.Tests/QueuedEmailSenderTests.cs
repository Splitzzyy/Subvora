using Microsoft.Extensions.Logging.Abstractions;
using SubVora.Infrastructure.Notifications;

namespace SubVora.Infrastructure.Tests;

public class QueuedEmailSenderTests
{
    private static QueuedEmailSender CreateSender() => new(NullLogger<QueuedEmailSender>.Instance);

    [Fact]
    public async Task SendAsync_ReturnsWithoutWaitingOnDelivery()
    {
        // The whole point: nothing about the caller's timing may depend on SMTP. Inline sending
        // made forgot-password answer in 15ms for an unknown address and 4.2s for a known one.
        var sender = CreateSender();

        await sender.SendAsync("user@example.com", "Subject", "Body");

        Assert.True(sender.Reader.TryRead(out var queued));
        Assert.Equal("user@example.com", queued!.ToEmail);
        Assert.Equal("Subject", queued.Subject);
        Assert.Equal("Body", queued.Body);
    }

    [Fact]
    public async Task SendAsync_WhenTheQueueIsFull_StillSucceedsForTheCaller()
    {
        // A full queue must look exactly like an empty one from the outside. Throwing here would
        // put the enumeration oracle straight back, just via a 500 instead of a delay.
        var sender = CreateSender();
        for (var i = 0; i < 2_000; i++)
        {
            await sender.SendAsync($"user{i}@example.com", "Subject", "Body");
        }

        var exception = await Record.ExceptionAsync(() => sender.SendAsync("overflow@example.com", "Subject", "Body"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task SendAsync_WhenTheQueueIsFull_LogsTheDropWithoutTheAddress()
    {
        // Logs must not carry email addresses: through forgot-password, an address in the log pairs
        // it with "a reset was requested", which the endpoint goes out of its way not to reveal.
        var logger = new CapturingLogger<QueuedEmailSender>();
        var sender = new QueuedEmailSender(logger);
        for (var i = 0; i < 1_000; i++)
        {
            await sender.SendAsync($"user{i}@example.com", "Subject", "Body");
        }

        await sender.SendAsync("overflow@example.com", "Your SubVora password reset code", "Body");

        var entry = Assert.Single(logger.Entries);
        Assert.Contains("Your SubVora password reset code", entry);
        Assert.DoesNotContain("@", entry);
    }

    [Fact]
    public async Task SendAsync_PreservesOrderForASingleReader()
    {
        var sender = CreateSender();

        await sender.SendAsync("first@example.com", "1", "Body");
        await sender.SendAsync("second@example.com", "2", "Body");

        Assert.True(sender.Reader.TryRead(out var first));
        Assert.True(sender.Reader.TryRead(out var second));
        Assert.Equal("first@example.com", first!.ToEmail);
        Assert.Equal("second@example.com", second!.ToEmail);
    }
}
