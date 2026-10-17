using System.Text.RegularExpressions;
using Serilog.Events;
using Serilog.Formatting;

namespace SubVora.Api;

/// <summary>
/// Masks anything shaped like an email address in each log event as it is written - the rendered
/// message, every property and the exception text alike - so "email addresses must not reach the
/// log sink" holds for log calls and exception messages nobody has audited yet, such as an SMTP
/// server's reply quoting the mailbox it refused.
/// </summary>
public sealed partial class EmailMaskingFormatter(ITextFormatter inner) : ITextFormatter
{
    public const string Mask = "[email]";

    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter();
        inner.Format(logEvent, buffer);
        output.Write(EmailAddress().Replace(buffer.ToString(), Mask));
    }

    // ponytail: shape-based, so it also masks a non-address that looks like one (user@host.tld in a
    // URL); acceptable for logs, where losing an address is the failure that matters.
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();
}
