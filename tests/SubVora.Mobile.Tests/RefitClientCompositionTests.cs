using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SubVora.Mobile;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;

namespace SubVora.Mobile.Tests;

/// <summary>
/// How the Refit clients are composed, asserted against the real container rather than a fake.
/// <para>
/// This is the check whose absence let two defects ship at once: <c>change-password</c> and
/// <c>logout</c> both lived on <see cref="IAuthApi"/>, which is registered <em>without</em>
/// <see cref="AuthDelegatingHandler"/>, so both called <c>[Authorize]</c> endpoints with no
/// Authorization header. Change-password answered 401 every time and was reported to the user as an
/// expired session; logout's revoke silently never happened while the client cleared its tokens and
/// moved on, leaving the refresh token live server-side for its full 30 days.
/// </para>
/// <para>
/// Every other mobile test substitutes a fake API, and the API tests call the endpoints directly
/// with a token, so nothing exercised the wiring itself. These tests do, by sending a real request
/// through the real handler pipeline into a capturing primary handler.
/// </para>
/// </summary>
public class RefitClientCompositionTests
{
    private const string StoredAccessToken = "stored-access-token";

    /// <summary>Captures the outbound request and answers with an empty JSON body.</summary>
    private sealed class CapturingPrimaryHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                // Deserializes as an empty list or a null object, whichever the method returns.
                Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// The app's own registrations, with the two platform-backed services swapped for fakes so the
    /// container can be built off-device. Everything about the HTTP pipeline is left exactly as
    /// <see cref="MauiProgram.AddSubVoraServices"/> composes it - that is what is under test.
    /// </summary>
    private static (ServiceProvider Provider, CapturingPrimaryHandler Handler) BuildProvider(string? accessToken = StoredAccessToken)
    {
        var services = new ServiceCollection();
        MauiProgram.AddSubVoraServices(services);

        // SecureStorage and FileSystem.AppDataDirectory both need a packaged app identity. Replacing
        // them is not weakening the test: neither is part of how a client is wired.
        services.AddSingleton<ITokenStore>(new FakeTokenStore { AccessToken = accessToken, RefreshToken = "stored-refresh-token" });
        services.AddSingleton<ILocalCacheService>(new FakeLocalCacheService());

        var capturing = new CapturingPrimaryHandler();

        // ConfigureAll applies to every named HttpClient, so this replaces the socket layer for all
        // of them without naming any - and without touching the DelegatingHandler chain above it,
        // which is the part being asserted.
        services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(options =>
            options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = capturing));

        return (services.BuildServiceProvider(), capturing);
    }

    public static TheoryData<string> AuthenticatedClients() =>
        new(nameof(IAccountApi), nameof(IUsersApi), nameof(ISubscriptionsApi), nameof(ICategoriesApi), nameof(IPaymentSourcesApi), nameof(IDashboardApi));

    /// <summary>
    /// One call per client that talks to <c>[Authorize]</c> endpoints. Each must arrive carrying the
    /// stored bearer token.
    /// </summary>
    [Theory]
    [MemberData(nameof(AuthenticatedClients))]
    public async Task EveryAuthenticatedClient_SendsTheStoredBearerToken(string clientName)
    {
        var (provider, capturing) = BuildProvider();
        await using var _ = provider;

        await CallAsync(provider, clientName);

        Assert.NotNull(capturing.LastRequest);
        Assert.NotNull(capturing.LastRequest!.Headers.Authorization);
        Assert.Equal("Bearer", capturing.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(StoredAccessToken, capturing.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task IAuthApi_SendsNoBearerToken()
    {
        // The negative half, and it is load-bearing: without it, "attach the handler to everything"
        // would pass. IAuthApi carries /auth/refresh, and chaining AuthDelegatingHandler there would
        // let a 401 during refresh recurse straight back into refresh.
        var (provider, capturing) = BuildProvider();
        await using var _ = provider;

        await provider.GetRequiredService<IAuthApi>()
            .LoginAsync(new LoginRequest { Email = "someone@example.com", Password = "irrelevant" });  // pragma: allowlist secret

        Assert.NotNull(capturing.LastRequest);
        Assert.Null(capturing.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task AnAuthenticatedClient_WithNoStoredToken_SendsNoAuthorizationHeader()
    {
        // Signed out, so there is nothing to attach. Proves the assertion above is reading the token
        // the store actually holds rather than a header that is always present.
        var (provider, capturing) = BuildProvider(accessToken: null);
        await using var _ = provider;

        await CallAsync(provider, nameof(IUsersApi));

        Assert.NotNull(capturing.LastRequest);
        Assert.Null(capturing.LastRequest!.Headers.Authorization);
    }

    /// <summary>
    /// Issues one real call per client. What comes back is deliberately ignored: the canned body
    /// does not fit every return type, and the assertion is about the request that went out, not the
    /// response that came back. The request is captured before any deserialization is attempted.
    /// </summary>
    private static async Task CallAsync(IServiceProvider provider, string clientName)
    {
        try
        {
            await (clientName switch
            {
                nameof(IAccountApi) => provider.GetRequiredService<IAccountApi>().LogoutAsync(new RefreshRequest { RefreshToken = "stored-refresh-token" }),
                nameof(IUsersApi) => (Task)provider.GetRequiredService<IUsersApi>().GetMeAsync(),
                nameof(ISubscriptionsApi) => provider.GetRequiredService<ISubscriptionsApi>().GetAllAsync(),
                nameof(ICategoriesApi) => provider.GetRequiredService<ICategoriesApi>().GetAllAsync(CancellationToken.None),
                nameof(IPaymentSourcesApi) => provider.GetRequiredService<IPaymentSourcesApi>().GetAllAsync(CancellationToken.None),
                nameof(IDashboardApi) => provider.GetRequiredService<IDashboardApi>().GetBurnRateAsync(),
                _ => throw new ArgumentOutOfRangeException(nameof(clientName), clientName, "Unknown client - add it to CallAsync and to AuthenticatedClients."),
            });
        }
        catch (Exception ex) when (ex is not ArgumentOutOfRangeException)
        {
            // Response-shape noise only. A wiring failure shows up as a missing header, not here.
        }
    }
}
