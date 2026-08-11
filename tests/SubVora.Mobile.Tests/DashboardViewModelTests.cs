using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class DashboardViewModelTests
{
    [Fact]
    public async Task LoadAsync_OnADefect_DoesNotLaunderItIntoTheCachedSnapshot()
    {
        // Same rule as the list: a bug of ours must not surface as yesterday's totals presented as
        // an offline fallback.
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate { Monthly = 99m, HomeCurrency = "USD" });
        var api = new FakeDashboardApi { Handler = () => throw new InvalidOperationException("defect") };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.LoadCommand.ExecuteAsync(null));

        Assert.False(viewModel.IsShowingCachedData);
        Assert.Equal(0m, viewModel.Monthly);
    }

    [Fact]
    public async Task LoadAsync_OnSuccess_MapsBurnRateResultOntoBindableProperties()
    {
        var burnRate = new BurnRateResult
        {
            Weekly = 12.50m,
            Monthly = 54.17m,
            Yearly = 650m,
            OneTimeThisYear = 20m,
            HomeCurrency = "USD",
            ByCategory =
            [
                new CategoryBreakdownItem { CategoryId = Guid.NewGuid(), CategoryName = "Streaming", MonthlyAmount = 30m },
                new CategoryBreakdownItem { CategoryId = null, CategoryName = "Uncategorized", MonthlyAmount = 24.17m },
            ],
        };
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) };
        var cache = new FakeLocalCacheService();
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(12.50m, viewModel.Weekly);
        Assert.Equal(54.17m, viewModel.Monthly);
        Assert.Equal(650m, viewModel.Yearly);
        Assert.Equal(20m, viewModel.OneTimeThisYear);
        Assert.Equal("USD", viewModel.HomeCurrency);
        Assert.Equal(2, viewModel.ByCategory.Count);
        Assert.Equal("Streaming", viewModel.ByCategory[0].CategoryName);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsShowingCachedData);

        var cached = await cache.GetAllAsync<CachedBurnRate>();
        Assert.Single(cached);
        Assert.Equal(12.50m, cached[0].Weekly);
    }

    [Fact]
    public async Task LoadAsync_OnApiFailureWithPopulatedCache_ShowsCachedFiguresAndSetsFlag()
    {
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate
        {
            Weekly = 5m,
            Monthly = 21.5m,
            Yearly = 258m,
            OneTimeThisYear = 0m,
            HomeCurrency = "EUR",
            ByCategory = [new CategoryBreakdownItem { CategoryName = "Music", MonthlyAmount = 21.5m }],
        });
        var api = new FakeDashboardApi { Handler = () => throw new HttpRequestException("network down") };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(5m, viewModel.Weekly);
        Assert.Equal("EUR", viewModel.HomeCurrency);
        Assert.Single(viewModel.ByCategory);
        Assert.True(viewModel.IsShowingCachedData);
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task LoadAsync_OnApiFailureWithEmptyCache_SetsErrorMessageInsteadOfCrashing()
    {
        var api = new FakeDashboardApi { Handler = () => throw new HttpRequestException("network down") };
        var cache = new FakeLocalCacheService();
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsShowingCachedData);
        Assert.Empty(viewModel.ByCategory);
    }

    [Fact]
    public async Task LoadAsync_WithSeveralPaymentSources_NamesTheOneCarryingTheMostSpend()
    {
        var burnRate = new BurnRateResult
        {
            Monthly = 100m,
            HomeCurrency = "INR",
            ByPaymentSource =
            [
                new PaymentSourceBreakdownItem { PaymentSourceId = Guid.NewGuid(), PaymentSourceLabel = "HDFC Card", MonthlyAmount = 75m },
                new PaymentSourceBreakdownItem { PaymentSourceId = Guid.NewGuid(), PaymentSourceLabel = "UPI", MonthlyAmount = 25m },
            ],
        };
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) };
        var cache = new FakeLocalCacheService();
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal("75% of your monthly spend goes to HDFC Card.", viewModel.TopSpendSourceSummary);
        Assert.Equal(2, viewModel.ByPaymentSource.Count);
        // Bars normalise against the leader, same as the category list.
        Assert.Equal(1d, viewModel.ByPaymentSource[0].Share, precision: 5);
        Assert.Equal(1d / 3d, viewModel.ByPaymentSource[1].Share, precision: 5);
    }

    [Fact]
    public async Task LoadAsync_WithASinglePaymentSource_SaysNothing()
    {
        // "100% of your spend goes to your only card" is not a finding, and a one-row breakdown has
        // nothing to compare against.
        var burnRate = new BurnRateResult
        {
            Monthly = 100m,
            HomeCurrency = "INR",
            ByPaymentSource = [new PaymentSourceBreakdownItem { PaymentSourceLabel = "HDFC Card", MonthlyAmount = 100m }],
        };
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) };
        var viewModel = new DashboardViewModel(api, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.TopSpendSourceSummary);
    }

    [Fact]
    public async Task LoadAsync_OnApiFailure_ShowsTheCachedPaymentSourceBreakdownToo()
    {
        // The offline path applies the same snapshot shape as the live one, so a field wired up on
        // only one of them is the failure this pins.
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate
        {
            Monthly = 40m,
            HomeCurrency = "INR",
            ByPaymentSource =
            [
                new PaymentSourceBreakdownItem { PaymentSourceLabel = "HDFC Card", MonthlyAmount = 30m },
                new PaymentSourceBreakdownItem { PaymentSourceLabel = "UPI", MonthlyAmount = 10m },
            ],
        });
        var api = new FakeDashboardApi { Handler = () => throw new HttpRequestException("network down") };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsShowingCachedData);
        Assert.Equal(2, viewModel.ByPaymentSource.Count);
        Assert.Equal("75% of your monthly spend goes to HDFC Card.", viewModel.TopSpendSourceSummary);
    }

    [Fact]
    public async Task LoadAsync_RoundTripsThePaymentSourceBreakdownThroughTheCache()
    {
        var burnRate = new BurnRateResult
        {
            Monthly = 40m,
            HomeCurrency = "INR",
            ByPaymentSource = [new PaymentSourceBreakdownItem { PaymentSourceLabel = "HDFC Card", MonthlyAmount = 40m }],
        };
        var cache = new FakeLocalCacheService();
        var api = new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        var cached = Assert.Single(await cache.GetAllAsync<CachedBurnRate>());
        Assert.Equal("HDFC Card", Assert.Single(cached.ByPaymentSource).PaymentSourceLabel);
    }

    [Fact]
    public async Task LoadAsync_WhenNothingIsExcludedAndRatesAreFresh_ShowsNoWarning()
    {
        var burnRate = new BurnRateResult
        {
            Monthly = 30m,
            HomeCurrency = "USD",
            OldestRateFetchedAt = DateTimeOffset.UtcNow.AddHours(-3),
        };
        var viewModel = new DashboardViewModel(new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) }, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Null(viewModel.WarningMessage);
    }

    [Fact]
    public async Task LoadAsync_WhenSubscriptionsAreExcluded_SaysHowManyAndWhy()
    {
        var burnRate = new BurnRateResult
        {
            Monthly = 30m,
            HomeCurrency = "USD",
            UnresolvedSubscriptionIds = [Guid.NewGuid(), Guid.NewGuid()],
        };
        var viewModel = new DashboardViewModel(new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) }, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.WarningMessage);
        Assert.Contains("2 subscriptions", viewModel.WarningMessage);
        Assert.Contains("no exchange rate available yet", viewModel.WarningMessage);
    }

    [Fact]
    public async Task LoadAsync_WithRatesOlderThanTheRefreshInterval_ReportsTheirAge()
    {
        var burnRate = new BurnRateResult
        {
            Monthly = 30m,
            HomeCurrency = "USD",
            OldestRateFetchedAt = DateTimeOffset.UtcNow.AddDays(-3),
        };
        var viewModel = new DashboardViewModel(new FakeDashboardApi { Handler = () => Task.FromResult(burnRate) }, new FakeLocalCacheService(), new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.WarningMessage);
        Assert.Contains("3 days ago", viewModel.WarningMessage);
    }

    [Fact]
    public async Task LoadAsync_FallingBackToCache_KeepsTheExclusionWarningFromTheLastSync()
    {
        var cache = new FakeLocalCacheService();
        await cache.UpsertAsync(new CachedBurnRate
        {
            Monthly = 21.5m,
            HomeCurrency = "EUR",
            UnresolvedSubscriptionCount = 1,
        });
        var api = new FakeDashboardApi { Handler = () => throw new HttpRequestException("network down") };
        var viewModel = new DashboardViewModel(api, cache, new WeakReferenceMessenger());

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsShowingCachedData);
        Assert.NotNull(viewModel.WarningMessage);
        Assert.Contains("1 subscription is", viewModel.WarningMessage);
    }
}
