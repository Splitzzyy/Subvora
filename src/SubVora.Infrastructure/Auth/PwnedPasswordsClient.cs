using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SubVora.Application.Auth;

namespace SubVora.Infrastructure.Auth;

/// <summary>
/// Have I Been Pwned's Pwned Passwords range API, by k-anonymity: only the first five hex
/// characters of the password's SHA-1 leave the server, and the match against the returned
/// suffixes happens here. No key, no account.
/// <para>
/// Fails open. An outage, a timeout or an unreadable answer lets the password through with a
/// warning, so registration and recovery never depend on a third party being up.
/// </para>
/// </summary>
public class PwnedPasswordsClient : IBreachedPasswordChecker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PwnedPasswordsClient> _logger;

    public PwnedPasswordsClient(HttpClient httpClient, ILogger<PwnedPasswordsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default)
    {
        // SHA-1 because that is the corpus's index, not as a password hash: nothing is stored.
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var (prefix, suffix) = (hash[..5], hash[5..]);

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"range/{prefix}");
            // Pads every answer to a similar size, so its length does not narrow down the prefix.
            request.Headers.Add("Add-Padding", "true");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "Breached-password check unavailable; allowing the password unchecked.");
            return false;
        }

        // "SUFFIX:COUNT" per line. Padding rows carry a count of 0 and are not breaches.
        foreach (var line in body.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator == 35
                && line.AsSpan(0, separator).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line.AsSpan(separator + 1).Trim(), out var count))
            {
                return count > 0;
            }
        }

        return false;
    }
}
