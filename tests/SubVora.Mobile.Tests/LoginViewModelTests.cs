using System.Net;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class LoginViewModelTests
{
    [Fact]
    public async Task LoginAsync_OnSuccess_SavesTokensAndRaisesLoginSucceeded()
    {
        var authApi = new FakeAuthApi();
        var tokenStore = new FakeTokenStore();
        var viewModel = new LoginViewModel(authApi, tokenStore) { Email = "user@example.com", Password = "correct-horse-battery-staple" };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal(FakeAuthApi.SampleTokens().AccessToken, tokenStore.AccessToken);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(authApi.LoginCalls);
    }

    [Fact]
    public async Task LoginAsync_On400_SurfacesFieldLevelErrorMessage()
    {
        var authApi = new FakeAuthApi
        {
            LoginHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(
                HttpStatusCode.BadRequest,
                content: null,
                validationErrorJson: """{"errors":{"Email":["'Email' is not a valid email address."]}}""")),
        };
        var tokenStore = new FakeTokenStore();
        var viewModel = new LoginViewModel(authApi, tokenStore) { Email = "not-an-email", Password = "x" };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("'Email' is not a valid email address.", viewModel.ErrorMessage);
        Assert.Null(tokenStore.AccessToken);
    }

    [Fact]
    public async Task LoginAsync_On401_SurfacesInvalidCredentialsMessageWithoutTouchingTokenStore()
    {
        var authApi = new FakeAuthApi
        {
            LoginHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.Unauthorized, content: null)),
        };
        var tokenStore = new FakeTokenStore();
        var viewModel = new LoginViewModel(authApi, tokenStore) { Email = "user@example.com", Password = "wrong-password" };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("Invalid email or password.", viewModel.ErrorMessage);
        Assert.Null(tokenStore.AccessToken);
        Assert.False(tokenStore.Cleared);
        Assert.Null(tokenStore.SavedTokens);
    }
}
