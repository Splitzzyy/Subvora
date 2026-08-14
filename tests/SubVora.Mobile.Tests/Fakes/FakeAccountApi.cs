using System.Net;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

/// <summary>
/// Minimal fake IAccountApi for ViewModel tests - no real HTTP involved.
/// <para>
/// Response builders are reused from <see cref="FakeAuthApi"/> rather than duplicated, so both fakes
/// construct <c>ApiResponse</c> the same way.
/// </para>
/// </summary>
public class FakeAccountApi : IAccountApi
{
    public Func<ChangePasswordRequest, Task<IApiResponse<AuthTokenResponse>>> ChangePasswordHandler =
        _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.OK, FakeAuthApi.SampleTokens()));

    public Func<RefreshRequest, Task<IApiResponse>> LogoutHandler =
        _ => Task.FromResult(FakeAuthApi.CreateResponse(HttpStatusCode.NoContent));

    public List<ChangePasswordRequest> ChangePasswordCalls { get; } = [];

    public List<RefreshRequest> LogoutCalls { get; } = [];

    public Task<IApiResponse<AuthTokenResponse>> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        ChangePasswordCalls.Add(request);
        return ChangePasswordHandler(request);
    }

    public Task<IApiResponse> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        LogoutCalls.Add(request);
        return LogoutHandler(request);
    }
}
