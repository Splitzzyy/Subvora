using System.Net;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class ForgotPasswordViewModelTests
{
    private static ForgotPasswordViewModel CreateViewModel(FakeAuthApi authApi, FakeConnectivityService? connectivity = null) =>
        new(authApi, connectivity ?? new FakeConnectivityService());

    [Fact]
    public async Task RequestCode_OnSuccess_MovesToTheCodeStep()
    {
        var authApi = new FakeAuthApi();
        var viewModel = CreateViewModel(authApi);
        viewModel.Email = "user@example.com";

        await viewModel.RequestCodeCommand.ExecuteAsync(null);

        Assert.True(viewModel.CodeSent);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(authApi.ForgotPasswordCalls);
    }

    [Fact]
    public async Task RequestCode_HedgesRatherThanConfirmingTheAccountExists()
    {
        // forgot-password answers 200 for an address it has never seen, deliberately. Saying "we've
        // sent you a code" would turn this screen into the enumeration oracle the endpoint refuses
        // to be.
        var viewModel = CreateViewModel(new FakeAuthApi());
        viewModel.Email = "stranger@example.com";

        await viewModel.RequestCodeCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.InfoMessage);
        Assert.StartsWith("If that address has an account", viewModel.InfoMessage);
    }

    [Fact]
    public async Task RequestCode_WithAMalformedAddress_StaysOnTheFirstStep()
    {
        var authApi = new FakeAuthApi
        {
            ForgotPasswordHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(
                HttpStatusCode.BadRequest,
                """{"errors":{"Email":["'Email' is not a valid email address."]}}""")),
        };
        var viewModel = CreateViewModel(authApi);
        viewModel.Email = "not-an-email";

        await viewModel.RequestCodeCommand.ExecuteAsync(null);

        Assert.False(viewModel.CodeSent);
        Assert.NotNull(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Reset_OnSuccess_RaisesPasswordReset()
    {
        var authApi = new FakeAuthApi();
        var viewModel = CreateViewModel(authApi);
        viewModel.Email = "user@example.com";
        viewModel.Code = "123456";
        viewModel.NewPassword = "an-entirely-different-passphrase";  // pragma: allowlist secret

        var raised = false;
        viewModel.PasswordReset += (_, _) => raised = true;

        await viewModel.ResetCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Reset_WithABadCode_ExplainsWithoutNavigating()
    {
        // The server returns one 400 for wrong, expired, used and over-the-limit - so a guesser
        // learns nothing. The client must not invent a more specific message either.
        var authApi = new FakeAuthApi
        {
            ResetPasswordHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.BadRequest)),
        };
        var viewModel = CreateViewModel(authApi);
        viewModel.Email = "user@example.com";
        viewModel.Code = "000000";

        var raised = false;
        viewModel.PasswordReset += (_, _) => raised = true;

        await viewModel.ResetCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Contains("isn't valid", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Reset_WhenOffline_SaysTheChangeDidNotLand()
    {
        var authApi = new FakeAuthApi { ResetPasswordHandler = _ => throw new HttpRequestException("Connection refused") };
        var viewModel = CreateViewModel(authApi, new FakeConnectivityService { IsConnected = false });
        viewModel.Email = "user@example.com";
        viewModel.Code = "123456";

        await viewModel.ResetCommand.ExecuteAsync(null);

        Assert.Equal("You're offline — this change wasn't saved. Try again once you're connected.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public void WithNoNetwork_TheButtonIsNotOffered() =>
        Assert.False(CreateViewModel(new FakeAuthApi(), new FakeConnectivityService { IsConnected = false }).CanSubmit);

    [Fact]
    public async Task RequestingAnotherCode_KeepsTheUserOnTheCodeStep()
    {
        // The "send another code" button reuses RequestCodeCommand. Dropping back to the first step
        // would discard a code the user may already have in their inbox.
        var authApi = new FakeAuthApi();
        var viewModel = CreateViewModel(authApi);
        viewModel.Email = "user@example.com";
        await viewModel.RequestCodeCommand.ExecuteAsync(null);

        await viewModel.RequestCodeCommand.ExecuteAsync(null);

        Assert.True(viewModel.CodeSent);
        Assert.Equal(2, authApi.ForgotPasswordCalls.Count);
    }
}
