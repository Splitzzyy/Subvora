using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SubVora.Application.Auth;

namespace SubVora.Api.Tests;

public class ChangePasswordControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string OriginalPassword = "correct-horse-battery-staple";  // pragma: allowlist secret
    private const string NewPassword = "an-entirely-different-passphrase";   // pragma: allowlist secret

    private readonly ApiWebApplicationFactory _factory;

    public ChangePasswordControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string Email, AuthTokenResponse Tokens)> RegisterAndSignInAsync()
    {
        var client = _factory.CreateClient();
        var email = $"changepw-{Guid.NewGuid()}@example.com";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = OriginalPassword });
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = OriginalPassword });
        var tokens = (await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return (client, email, tokens);
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string current, string replacement) =>
        client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest
        {
            CurrentPassword = current,
            NewPassword = replacement,
        });

    [Fact]
    public async Task ChangePassword_WithTheCorrectCurrentPassword_SwapsTheCredential()
    {
        var (client, email, _) = await RegisterAndSignInAsync();

        var response = await ChangeAsync(client, OriginalPassword, NewPassword);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var anonymous = _factory.CreateClient();

        var withNew = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, withNew.StatusCode);

        var withOld = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = OriginalPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, withOld.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ReturnsAWorkingTokenPairSoTheCallersDeviceStaysSignedIn()
    {
        // The sweep below revokes the caller's own refresh token too. Without a replacement the
        // user is signed out of the device they just used, which reads as the app breaking.
        var (client, _, _) = await RegisterAndSignInAsync();

        var response = await ChangeAsync(client, OriginalPassword, NewPassword);
        var replacement = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(replacement);
        Assert.False(string.IsNullOrWhiteSpace(replacement!.AccessToken));

        var refreshed = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = replacement.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_EvictsEveryOtherDevice()
    {
        // A refresh token lives 30 days and would otherwise keep minting access tokens off the
        // password that was just replaced - the whole point of changing it.
        var (client, email, _) = await RegisterAndSignInAsync();

        var otherDevice = _factory.CreateClient();
        var otherLogin = await otherDevice.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = OriginalPassword });
        var otherTokens = (await otherLogin.Content.ReadFromJsonAsync<AuthTokenResponse>())!;

        await ChangeAsync(client, OriginalPassword, NewPassword);

        var stale = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshRequest { RefreshToken = otherTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_Returns400AndChangesNothing()
    {
        // 400 rather than 401: the caller is authenticated, it is the body that is wrong. A 401
        // would send the client into its token-refresh path for no reason.
        var (client, email, _) = await RegisterAndSignInAsync();

        var response = await ChangeAsync(client, "not-the-current-password", NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stillWorks = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = email, Password = OriginalPassword });
        Assert.Equal(HttpStatusCode.OK, stillWorks.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithoutAuthentication_Returns401()
    {
        var response = await ChangeAsync(_factory.CreateClient(), OriginalPassword, NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ToSomethingShorterThanRegistrationAllows_Returns400()
    {
        var (client, _, _) = await RegisterAndSignInAsync();

        var response = await ChangeAsync(client, OriginalPassword, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ToTheSamePassword_Returns400()
    {
        var (client, _, _) = await RegisterAndSignInAsync();

        var response = await ChangeAsync(client, OriginalPassword, OriginalPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
