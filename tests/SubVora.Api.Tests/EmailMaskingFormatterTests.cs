using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace SubVora.Api.Tests;

/// <summary>Through a real Serilog pipeline with the formatter Program.cs uses, not a stand-in logger.</summary>
public class EmailMaskingFormatterTests
{
    private const string Address = "user@example.com";

    [Fact]
    public void AnExceptionQuotingAnAddress_ReachesTheSinkMasked()
    {
        // The shape an SMTP rejection takes: the server's reply quotes the mailbox it refused.
        var output = Write(log => log.Warning(
            new InvalidOperationException($"5.1.1 <{Address}>: Recipient address rejected"),
            "Could not deliver an email ({Subject}).",
            "Your SubVora password reset code"));

        Assert.DoesNotContain(Address, output);
        Assert.Contains($"<{EmailMaskingFormatter.Mask}>: Recipient address rejected", output);
        Assert.Contains("Your SubVora password reset code", output);
    }

    [Fact]
    public void APropertyHoldingAnAddress_IsMaskedToo()
    {
        var output = Write(log => log.Information("Signed in {Email} from {Mailbox}", Address, $"Someone <{Address.ToUpperInvariant()}>"));

        Assert.DoesNotContain(Address, output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(EmailMaskingFormatter.Mask, output);
    }

    private static string Write(Action<ILogger> log)
    {
        var output = new StringWriter();
        using (var logger = new LoggerConfiguration()
            .WriteTo.Sink(new FormattingSink(new EmailMaskingFormatter(new CompactJsonFormatter()), output))
            .CreateLogger())
        {
            log(logger);
        }

        return output.ToString();
    }

    private sealed class FormattingSink(ITextFormatter formatter, TextWriter output) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => formatter.Format(logEvent, output);
    }
}
