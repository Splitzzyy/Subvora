using SubVora.Mobile.Api;

namespace SubVora.Mobile.Tests;

public class ApiConfigTests
{
    [Fact]
    public void BaseAddress_IsAnAbsoluteUriWithATrailingSlash()
    {
        // MauiProgram feeds this straight to new Uri(...), and Refit's relative paths only resolve
        // against a base address that ends in a slash - a build-time override is easy to hand-write
        // without one.
        Assert.True(Uri.TryCreate(ApiConfig.BaseAddress, UriKind.Absolute, out _));
        Assert.EndsWith("/", ApiConfig.BaseAddress);
    }

    [Fact]
    public void BaseAddress_OffAndroid_DoesNotUseTheEmulatorLoopbackAlias()
    {
        // 10.0.2.2 is the Android emulator's alias for the host machine and reaches a stranger
        // anywhere else. This test project builds for Windows, so the substitution must have run.
        Assert.DoesNotContain("10.0.2.2", ApiConfig.BaseAddress);
    }

    [Fact]
    public void RequestTimeout_IsWellShortOfHttpClientsDefault()
    {
        // The default is 100 seconds. An unreachable host that refuses a connection fails instantly,
        // but one that swallows it - a dead adb tunnel, a sleeping free-tier instance - left the
        // user on a spinner for over a minute and a half before the offline message appeared.
        Assert.True(ApiConfig.RequestTimeout < TimeSpan.FromSeconds(100));
        Assert.True(ApiConfig.RequestTimeout >= TimeSpan.FromSeconds(20), "must still clear a slow cold start");
    }

    [Fact]
    public void RefreshTimeout_IsShorterThanARegularRequest()
    {
        // A refresh runs inside another request's 401 retry, so its wait stacks on top of the
        // original's. Sharing the full timeout would let one user action stall for both.
        Assert.True(ApiConfig.RefreshTimeout < ApiConfig.RequestTimeout);
        Assert.True(ApiConfig.RefreshTimeout > TimeSpan.Zero);
    }
}
