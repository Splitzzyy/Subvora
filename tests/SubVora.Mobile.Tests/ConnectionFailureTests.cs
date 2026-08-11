using CommunityToolkit.Mvvm.Messaging;
using Refit;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Refit 13 wraps a failed-to-connect request in <see cref="ApiRequestException"/> rather than
/// letting HttpClient's own <see cref="HttpRequestException"/> through, as Refit 12 did. Every view
/// model catches with <c>when (ApiErrorMapper.IsApiFailure(ex))</c>, so the moment that filter
/// stopped matching the most common failure there is, an unreachable API stopped being handled and
/// started crashing the process:
/// <code>
/// FATAL EXCEPTION: main
/// android.runtime.JavaProxyThrowable: [Refit.ApiRequestException]: Connection failure
///   at SubVora.Mobile.ViewModels.DashboardViewModel+&lt;LoadAsync&gt;d__44.MoveNext
///   at CommunityToolkit.Mvvm.Input.AsyncRelayCommand+&lt;AwaitAndThrowIfFailed&gt;
/// </code>
/// </summary>
public class ConnectionFailureTests
{
    [Fact]
    public void ConnectionFailure_IsTreatedAsAnApiFailure()
    {
        // The one assertion the crash came down to.
        Assert.True(ApiErrorMapper.IsApiFailure(TestApiExceptions.ConnectionFailure()));
    }

    [Fact]
    public void ConnectionFailure_IsNotAnApiException()
    {
        // Why the old filter missed it: it is a sibling of ApiException, not a subclass. If this
        // ever becomes false the filter could be simplified - until then it must not be.
        Assert.IsNotType<ApiException>(TestApiExceptions.ConnectionFailure());
    }

    [Fact]
    public void ConnectionFailure_ReadsAsOffline_NotAsAGenericFault()
    {
        Assert.Equal("You appear to be offline.", ApiErrorMapper.ToDisplayMessage(TestApiExceptions.ConnectionFailure()));
    }

    [Fact]
    public void ConnectionFailure_OnAWrite_SaysTheChangeWasNotSaved()
    {
        // There is no write queue - the change is gone. "You appear to be offline" would let the
        // user walk away believing it will sync.
        Assert.Equal(
            "You're offline — this change wasn't saved. Try again once you're connected.",
            ApiErrorMapper.ToWriteFailureMessage(TestApiExceptions.ConnectionFailure()));
    }

    [Fact]
    public void ADefectIsStillNotSwallowedAsANetworkProblem()
    {
        // The filter must stay narrow: a NullReferenceException is a bug and should surface as one.
        Assert.False(ApiErrorMapper.IsApiFailure(new NullReferenceException()));
        Assert.False(ApiErrorMapper.IsApiFailure(new InvalidOperationException()));
    }

    [Fact]
    public async Task Dashboard_OnConnectionFailure_FallsBackToCacheInsteadOfThrowing()
    {
        // The exact crash path from the logcat trace, end to end through the command.
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate { Monthly = 21.5m, HomeCurrency = "INR" });
        var api = new FakeDashboardApi { Handler = () => throw TestApiExceptions.ConnectionFailure() };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(21.5m, viewModel.Monthly);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Dashboard_OnConnectionFailureWithNoCache_ShowsAMessageInsteadOfThrowing()
    {
        var api = new FakeDashboardApi { Handler = () => throw TestApiExceptions.ConnectionFailure() };
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("You appear to be offline.", viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task SubscriptionList_OnConnectionFailure_DoesNotThrow()
    {
        // Same filter, nine view models. Spot-checking a second one guards against the fix being
        // applied to the Dashboard's catch block alone rather than to the shared mapper.
        var api = new FakeSubscriptionsApi { GetAllHandler = () => throw TestApiExceptions.ConnectionFailure() };
        var viewModel = new SubscriptionListViewModel(
            api,
            new FakeLocalCacheService(),
            new FakeUserPrompt(),
            new WeakReferenceMessenger(),
            new FakeRenewalNotificationScheduler(),
            new FakeConnectivityService());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
    }
}
