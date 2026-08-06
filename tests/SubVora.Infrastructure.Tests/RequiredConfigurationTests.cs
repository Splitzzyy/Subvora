using Microsoft.Extensions.Configuration;
using SubVora.Infrastructure.Configuration;

namespace SubVora.Infrastructure.Tests;

public class RequiredConfigurationTests
{
    private static IConfiguration Build(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();

    [Fact]
    public void GetRequired_ReturnsTheConfiguredValue()
    {
        Assert.Equal("a-real-value", Build(("Jwt:Secret", "a-real-value")).GetRequired("Jwt:Secret"));
    }

    [Fact]
    public void GetRequired_MissingKey_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build().GetRequired("Jwt:Secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRequired_BlankValue_Throws(string blank)
    {
        // The case a plain null check misses: appsettings.json ships Jwt:Secret as "" so no secret
        // sits in source control, and letting that through boots the API with an empty signing key.
        var exception = Assert.Throws<InvalidOperationException>(() => Build(("Jwt:Secret", blank)).GetRequired("Jwt:Secret"));

        Assert.Contains("Jwt:Secret", exception.Message);
    }

    [Fact]
    public void GetRequiredConnectionString_BlankValue_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(("ConnectionStrings:Default", "")).GetRequiredConnectionString("Default"));
    }

    [Theory]
    [InlineData("dev")]
    [InlineData("0123456789012345678901234567890")] // 31 bytes - one short of HS256's minimum.
    public void GetRequiredJwtSecret_TooShortToSignWith_ThrowsAtStartupRatherThanOnFirstLogin(string tooShort)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(("Jwt:Secret", tooShort)).GetRequiredJwtSecret());

        Assert.Contains("32 bytes", exception.Message);
    }

    [Fact]
    public void GetRequiredJwtSecret_LongEnough_IsReturned()
    {
        var secret = new string('k', 32);

        Assert.Equal(secret, Build(("Jwt:Secret", secret)).GetRequiredJwtSecret());
    }

    [Fact]
    public void GetRequiredJwtSecret_BlankValue_StillThrowsTheMissingValueMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Build(("Jwt:Secret", "")).GetRequiredJwtSecret());

        Assert.Contains("not configured", exception.Message);
    }
}
