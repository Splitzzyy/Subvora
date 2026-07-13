using SubVora.Mobile.Api.Dtos;
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
        var viewModel = new DashboardViewModel(api);

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
    }

    [Fact]
    public async Task LoadAsync_OnApiFailure_SetsErrorMessageInsteadOfCrashing()
    {
        var api = new FakeDashboardApi { Handler = () => throw new HttpRequestException("network down") };
        var viewModel = new DashboardViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.IsLoading);
        Assert.Empty(viewModel.ByCategory);
    }
}
