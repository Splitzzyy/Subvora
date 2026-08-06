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
}
