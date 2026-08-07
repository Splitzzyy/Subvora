using System.Net;
using System.Net.Http.Headers;

namespace SubVora.Mobile.Services;

/// <summary>
/// Attaches the stored access token to outbound requests and transparently refreshes it once on a 401.
/// Registered as the attached handler for every Refit client except <see cref="Api.IAuthApi"/>,
/// which must not loop through this handler for its own login/register/refresh calls.
/// <para>
/// Must be registered transient: HttpClientFactory sets <c>InnerHandler</c> on whichever instance it
/// is handed, and throws on one that already has a pipeline. Everything that genuinely has to be
/// shared between clients lives in <see cref="SessionRefresher"/> instead.
/// </para>
/// </summary>
public class AuthDelegatingHandler : DelegatingHandler
{
    private readonly ITokenStore _tokenStore;
    private readonly SessionRefresher _sessionRefresher;

    public AuthDelegatingHandler(ITokenStore tokenStore, SessionRefresher sessionRefresher)
    {
        _tokenStore = tokenStore;
        _sessionRefresher = sessionRefresher;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var accessToken = await _tokenStore.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var freshAccessToken = await _sessionRefresher.RefreshOnceAsync(accessToken, cancellationToken);
        if (freshAccessToken is null)
        {
            return response;
        }

        response.Dispose();

        var retryRequest = await CloneRequestAsync(request);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", freshAccessToken);
        return await base.SendAsync(retryRequest, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
