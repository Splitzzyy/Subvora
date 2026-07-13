using Microsoft.Maui.Storage;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Services;

public class SecureStorageTokenStore : ITokenStore
{
    private const string AccessTokenKey = "auth_access_token";
    private const string RefreshTokenKey = "auth_refresh_token";

    public Task<string?> GetAccessTokenAsync() => SecureStorage.Default.GetAsync(AccessTokenKey);

    public Task<string?> GetRefreshTokenAsync() => SecureStorage.Default.GetAsync(RefreshTokenKey);

    public async Task SaveTokensAsync(AuthTokenResponse tokens)
    {
        await SecureStorage.Default.SetAsync(AccessTokenKey, tokens.AccessToken);
        await SecureStorage.Default.SetAsync(RefreshTokenKey, tokens.RefreshToken);
    }

    public Task ClearAsync()
    {
        SecureStorage.Default.Remove(AccessTokenKey);
        SecureStorage.Default.Remove(RefreshTokenKey);
        return Task.CompletedTask;
    }
}
