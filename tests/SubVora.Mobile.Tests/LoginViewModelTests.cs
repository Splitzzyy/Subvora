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
        var viewModel = new LoginViewModel(authApi, tokenStore, new FakeDevicesApi(), new FakePushTokenProvider())
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",
        };

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
        var viewModel = new LoginViewModel(authApi, tokenStore, new FakeDevicesApi(), new FakePushTokenProvider())
        {
            Email = "not-an-email",
            Password = "x",
        };

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
        var viewModel = new LoginViewModel(authApi, tokenStore, new FakeDevicesApi(), new FakePushTokenProvider())
        {
            Email = "user@example.com",
            Password = "wrong-password",
        };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("Invalid email or password.", viewModel.ErrorMessage);
        Assert.Null(tokenStore.AccessToken);
        Assert.False(tokenStore.Cleared);
        Assert.Null(tokenStore.SavedTokens);
    }

    [Fact]
    public async Task LoginAsync_OnSuccess_RegistersThePushTokenWithItsPlatform()
    {
        var devicesApi = new FakeDevicesApi();
        var pushTokenProvider = new FakePushTokenProvider
        {
            Platform = "iOS",
            GetTokenHandler = () => Task.FromResult<string?>("apns-backed-fcm-token"),
        };
        var viewModel = new LoginViewModel(new FakeAuthApi(), new FakeTokenStore(), devicesApi, pushTokenProvider)
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",
        };

        await viewModel.LoginCommand.ExecuteAsync(null);

        var call = Assert.Single(devicesApi.RegisterCalls);
        Assert.Equal("apns-backed-fcm-token", call.Token);
        Assert.Equal("iOS", call.Platform);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task LoginAsync_WithNoPushToken_RegistersNothingAndStillSucceeds(string? token)
    {
        var devicesApi = new FakeDevicesApi();
        var pushTokenProvider = new FakePushTokenProvider { GetTokenHandler = () => Task.FromResult(token) };
        var viewModel = new LoginViewModel(new FakeAuthApi(), new FakeTokenStore(), devicesApi, pushTokenProvider)
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",
        };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.Empty(devicesApi.RegisterCalls);
        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoginAsync_WhenPushRegistrationFails_DoesNotFailTheLogin()
    {
        var devicesApi = new FakeDevicesApi
        {
            RegisterHandler = _ => throw new HttpRequestException("device registration is down"),
        };
        var viewModel = new LoginViewModel(new FakeAuthApi(), new FakeTokenStore(), devicesApi, new FakePushTokenProvider())
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",
        };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task LoginAsync_WhenTheTokenProviderThrows_DoesNotFailTheLogin()
    {
        var pushTokenProvider = new FakePushTokenProvider
        {
            GetTokenHandler = () => throw new InvalidOperationException("messaging SDK not initialised"),
        };
        var viewModel = new LoginViewModel(new FakeAuthApi(), new FakeTokenStore(), new FakeDevicesApi(), pushTokenProvider)
        {
            Email = "user@example.com",
            Password = "correct-horse-battery-staple",
        };

        var raised = false;
        viewModel.LoginSucceeded += (_, _) => raised = true;

        await viewModel.LoginCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }
}
