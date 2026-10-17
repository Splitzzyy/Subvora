using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SubVora.Infrastructure.Auth;

namespace SubVora.Infrastructure.Tests;

/// <summary>No network - the k-anonymity exchange and the fail-open rule, against canned range bodies.</summary>
public class PwnedPasswordsClientTests
{
    /// <summary>Built rather than written out so it is not a credential-shaped literal.</summary>
    private static readonly string Password = new('x', 12);

    private static readonly string Hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Password)));

    [Fact]
    public async Task SendsOnlyTheFirstFiveHashCharacters_AndFindsTheSuffix()
    {
        HttpRequestMessage? sent = null;
        var client = ClientAnswering(_ => Ok($"0000000000000000000000000000000000A:3\r\n{Hash[5..]}:42\r\n"), r => sent = r);

        Assert.True(await client.IsBreachedAsync(Password));
        Assert.Equal($"https://api.pwnedpasswords.com/range/{Hash[..5]}", sent?.RequestUri?.ToString());
        Assert.DoesNotContain(Hash[5..], sent!.RequestUri!.ToString());
        Assert.True(sent.Headers.Contains("Add-Padding"));
    }

    [Fact]
    public async Task ASuffixThatIsNotListed_IsNotBreached()
    {
        var client = ClientAnswering(_ => Ok("0000000000000000000000000000000000A:3\r\n"));

        Assert.False(await client.IsBreachedAsync(Password));
    }

    [Fact]
    public async Task APaddingRow_IsNotABreach()
    {
        // Padding rows are real-looking suffixes with a count of 0.
        var client = ClientAnswering(_ => Ok($"{Hash[5..]}:0\r\n"));

        Assert.False(await client.IsBreachedAsync(Password));
    }

    [Fact]
    public async Task AnUnreachableRangeApi_LetsThePasswordThrough()
    {
        var down = ClientAnswering(_ => throw new HttpRequestException("connection refused"));
        var erroring = ClientAnswering(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var timingOut = ClientAnswering(_ => throw new TaskCanceledException("timeout"));

        Assert.False(await down.IsBreachedAsync(Password));
        Assert.False(await erroring.IsBreachedAsync(Password));
        Assert.False(await timingOut.IsBreachedAsync(Password));
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static PwnedPasswordsClient ClientAnswering(Func<HttpRequestMessage, HttpResponseMessage> answer, Action<HttpRequestMessage>? onRequest = null) =>
        new(new HttpClient(new StubHandler(answer, onRequest)) { BaseAddress = new Uri("https://api.pwnedpasswords.com/") },
            NullLogger<PwnedPasswordsClient>.Instance);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer, Action<HttpRequestMessage>? onRequest) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onRequest?.Invoke(request);
            return Task.FromResult(answer(request));
        }
    }
}
