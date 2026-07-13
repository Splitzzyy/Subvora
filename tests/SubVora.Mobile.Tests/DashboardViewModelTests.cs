using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class DashboardViewModelTests
{
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
        var viewModel = new DashboardViewModel(api, cache);

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
        var viewModel = new DashboardViewModel(api, cache);

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
        var viewModel = new DashboardViewModel(api, cache);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.False(viewModel.IsShowingCachedData);
        Assert.Empty(viewModel.ByCategory);
    }
}
