using System.Net;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class RegisterViewModelTests
{
    [Fact]
    public async Task RegisterAsync_OnSuccess_LogsInAndSavesTokensAndRaisesRegisterSucceeded()
    {
        var authApi = new FakeAuthApi();
        var tokenStore = new FakeTokenStore();
        var viewModel = new RegisterViewModel(authApi, tokenStore)
        {
            Email = "new-user@example.com",
            Password = "correct-horse-battery-staple",
            ConfirmPassword = "correct-horse-battery-staple",
        };

        var raised = false;
        viewModel.RegisterSucceeded += (_, _) => raised = true;

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Equal(FakeAuthApi.SampleTokens().AccessToken, tokenStore.AccessToken);
        Assert.Null(viewModel.ErrorMessage);
        Assert.Single(authApi.RegisterCalls);
        Assert.Single(authApi.LoginCalls);
    }

    [Fact]
    public async Task RegisterAsync_WithMismatchedPasswords_SurfacesErrorWithoutCallingApi()
    {
        var authApi = new FakeAuthApi();
        var tokenStore = new FakeTokenStore();
        var viewModel = new RegisterViewModel(authApi, tokenStore)
        {
            Email = "new-user@example.com",
            Password = "correct-horse-battery-staple",
            ConfirmPassword = "different-password",
        };

        var raised = false;
        viewModel.RegisterSucceeded += (_, _) => raised = true;

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("Passwords do not match.", viewModel.ErrorMessage);
        Assert.Empty(authApi.RegisterCalls);
    }

    [Fact]
    public async Task RegisterAsync_On400_SurfacesFieldLevelErrorMessage()
    {
        var authApi = new FakeAuthApi
        {
            RegisterHandler = _ => Task.FromResult(FakeAuthApi.CreateResponse(
                HttpStatusCode.BadRequest,
                validationErrorJson: """{"errors":{"Password":["'Password' must be at least 8 characters."]}}""")),
        };
        var tokenStore = new FakeTokenStore();
        var viewModel = new RegisterViewModel(authApi, tokenStore)
        {
            Email = "new-user@example.com",
            Password = "short",
            ConfirmPassword = "short",
        };

        var raised = false;
        viewModel.RegisterSucceeded += (_, _) => raised = true;

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("'Password' must be at least 8 characters.", viewModel.ErrorMessage);
        Assert.Null(tokenStore.AccessToken);
        Assert.Empty(authApi.LoginCalls);
    }
}
