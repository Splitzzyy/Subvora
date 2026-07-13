using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Services;

public interface ITokenStore
{
    Task<string?> GetAccessTokenAsync();
    Task<string?> GetRefreshTokenAsync();
    Task SaveTokensAsync(AuthTokenResponse tokens);
    Task ClearAsync();
}
