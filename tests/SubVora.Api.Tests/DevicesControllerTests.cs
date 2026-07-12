using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SubVora.Application.Auth;
using SubVora.Application.Devices;

namespace SubVora.Api.Tests;

public class DevicesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DevicesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var email = $"devices-{Guid.NewGuid()}@example.com";
        const string password = "correct-horse-battery-staple";

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task RegisterDevice_NewToken_Creates()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new RegisterDeviceTokenRequest { Token = $"token-{Guid.NewGuid()}", Platform = "Android" };

        var response = await client.PostAsJsonAsync("/api/v1/devices", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceTokenDto>();
        Assert.NotNull(body);
        Assert.Equal(request.Token, body!.Token);
        Assert.Equal("Android", body.Platform);
    }

    [Fact]
    public async Task RegisterDevice_ExistingToken_UpdatesLastSeen()
    {
        var client = await CreateAuthenticatedClientAsync();
        var request = new RegisterDeviceTokenRequest { Token = $"token-{Guid.NewGuid()}", Platform = "iOS" };

        var first = await client.PostAsJsonAsync("/api/v1/devices", request);
        var firstBody = await first.Content.ReadFromJsonAsync<DeviceTokenDto>();

        await Task.Delay(50);

        var second = await client.PostAsJsonAsync("/api/v1/devices", request);
        var secondBody = await second.Content.ReadFromJsonAsync<DeviceTokenDto>();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.True(secondBody.LastSeenAt > firstBody.LastSeenAt);
    }

    [Fact]
    public async Task RegisterDevice_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var request = new RegisterDeviceTokenRequest { Token = "some-token", Platform = "Android" };

        var response = await client.PostAsJsonAsync("/api/v1/devices", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
