using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Auth;
using SubVora.Application.PaymentSources;
using SubVora.Domain.Enums;

namespace SubVora.Api.Tests;

public class PaymentSourcesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    // The server serializes enums as strings (JsonStringEnumConverter, configured in
    // Program.cs); HttpContentJsonExtensions defaults don't pick that up automatically.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ApiWebApplicationFactory _factory;

    public PaymentSourcesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email)
    {
        var client = _factory.CreateClient();
        const string password = "correct-horse-battery-staple";

        var registerResponse = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokens = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
        return client;
    }

    [Fact]
    public async Task GetPaymentSources_ReturnsOnlyCallersOwn()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"ps-owner-{Guid.NewGuid()}@example.com");
        var otherClient = await CreateAuthenticatedClientAsync($"ps-other-{Guid.NewGuid()}@example.com");

        var ownerLabel = $"Visa-{Guid.NewGuid()}";
        var ownerCreate = await ownerClient.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = ownerLabel, SourceType = PaymentSourceType.Card }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, ownerCreate.StatusCode);

        var otherCreate = await otherClient.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = $"PayPal-{Guid.NewGuid()}", SourceType = PaymentSourceType.Wallet }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, otherCreate.StatusCode);

        var listResponse = await ownerClient.GetAsync("/api/v1/payment-sources");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<PaymentSourceDto>>(JsonOptions);

        Assert.NotNull(list);
        Assert.Contains(list!, p => p.Label == ownerLabel);
        Assert.DoesNotContain(list!, p => p.Label.StartsWith("PayPal-"));
    }

    [Fact]
    public async Task GetPaymentSources_WithoutAuth_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/payment-sources");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePaymentSource_AddsRecordWithSourceType()
    {
        var client = await CreateAuthenticatedClientAsync($"ps-create-{Guid.NewGuid()}@example.com");
        var label = $"Chase Visa •4021-{Guid.NewGuid()}";

        var response = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = label, SourceType = PaymentSourceType.Card }, JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(label, dto!.Label);
        Assert.Equal(PaymentSourceType.Card, dto.SourceType);
        Assert.NotEqual(Guid.Empty, dto.Id);
    }

    [Fact]
    public async Task CreatePaymentSource_WithEmptyLabel_Returns400()
    {
        var client = await CreateAuthenticatedClientAsync($"ps-invalid-{Guid.NewGuid()}@example.com");

        var response = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = "", SourceType = PaymentSourceType.Other }, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeletePaymentSource_ForAnotherUsersRecord_Returns404()
    {
        var ownerClient = await CreateAuthenticatedClientAsync($"ps-del-owner-{Guid.NewGuid()}@example.com");
        var attackerClient = await CreateAuthenticatedClientAsync($"ps-del-attacker-{Guid.NewGuid()}@example.com");

        var createResponse = await ownerClient.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = $"Owner-{Guid.NewGuid()}", SourceType = PaymentSourceType.BankAccount }, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);

        var deleteResponse = await attackerClient.DeleteAsync($"/api/v1/payment-sources/{created!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);

        var ownerListResponse = await ownerClient.GetAsync("/api/v1/payment-sources");
        var ownerList = await ownerListResponse.Content.ReadFromJsonAsync<List<PaymentSourceDto>>(JsonOptions);
        Assert.Contains(ownerList!, p => p.Id == created.Id);
    }

    [Fact]
    public async Task DeletePaymentSource_OwnRecord_Succeeds()
    {
        var client = await CreateAuthenticatedClientAsync($"ps-del-own-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = $"ToDelete-{Guid.NewGuid()}", SourceType = PaymentSourceType.Other }, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);

        var deleteResponse = await client.DeleteAsync($"/api/v1/payment-sources/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/payment-sources");
        var list = await listResponse.Content.ReadFromJsonAsync<List<PaymentSourceDto>>(JsonOptions);
        Assert.DoesNotContain(list!, p => p.Id == created.Id);
    }

    [Fact]
    public async Task DeletePaymentSource_AlreadyDeleted_Returns404()
    {
        var client = await CreateAuthenticatedClientAsync($"ps-del-twice-{Guid.NewGuid()}@example.com");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payment-sources", new CreatePaymentSourceRequest { Label = $"Twice-{Guid.NewGuid()}", SourceType = PaymentSourceType.Other }, JsonOptions);
        var created = await createResponse.Content.ReadFromJsonAsync<PaymentSourceDto>(JsonOptions);

        var firstDelete = await client.DeleteAsync($"/api/v1/payment-sources/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);

        var secondDelete = await client.DeleteAsync($"/api/v1/payment-sources/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, secondDelete.StatusCode);
    }
}
