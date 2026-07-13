using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeTokenStore : ITokenStore
{
    public string? AccessToken;
    public string? RefreshToken;
    public bool Cleared;
    public AuthTokenResponse? SavedTokens;

    public Task<string?> GetAccessTokenAsync() => Task.FromResult(AccessToken);

    public Task<string?> GetRefreshTokenAsync() => Task.FromResult(RefreshToken);

    public Task SaveTokensAsync(AuthTokenResponse tokens)
    {
        SavedTokens = tokens;
        AccessToken = tokens.AccessToken;
        RefreshToken = tokens.RefreshToken;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        Cleared = true;
        AccessToken = null;
        RefreshToken = null;
        return Task.CompletedTask;
    }
}
