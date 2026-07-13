using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class SubscriptionDetailViewModelTests
{
    private static SubscriptionDetailViewModel CreateViewModel(
        FakeSubscriptionsApi? subscriptionsApi = null,
        FakeCategoriesApi? categoriesApi = null,
        FakePaymentSourcesApi? paymentSourcesApi = null) =>
        new(subscriptionsApi ?? new FakeSubscriptionsApi(), categoriesApi ?? new FakeCategoriesApi(), paymentSourcesApi ?? new FakePaymentSourcesApi());

    [Fact]
    public async Task LoadPickersAsync_PopulatesCategoriesAndPaymentSourcesFromApis()
    {
        var categoriesApi = new FakeCategoriesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" }]),
        };
        var paymentSourcesApi = new FakePaymentSourcesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa" }]),
        };
        var viewModel = CreateViewModel(categoriesApi: categoriesApi, paymentSourcesApi: paymentSourcesApi);

        await viewModel.LoadPickersCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Categories);
        Assert.Equal("Streaming", viewModel.Categories[0].Name);
        Assert.Single(viewModel.PaymentSources);
        Assert.Equal("Visa", viewModel.PaymentSources[0].Label);
    }

    [Fact]
    public async Task SaveAsync_WithValidData_CallsCreateAndRaisesSaveSucceeded()
    {
        var subscriptionsApi = new FakeSubscriptionsApi();
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var paymentSource = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa" };
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi);
        viewModel.CustomName = "Netflix";
        viewModel.CostAmount = 15.99m;
        viewModel.Currency = "USD";
        viewModel.SelectedCategory = category;
        viewModel.SelectedPaymentSource = paymentSource;

        var raised = false;
        viewModel.SaveSucceeded += (_, _) => raised = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
        var request = Assert.Single(subscriptionsApi.CreateCalls);
        Assert.Equal("Netflix", request.CustomName);
        Assert.Equal(15.99m, request.CostAmount);
        Assert.Equal(category.Id, request.CategoryId);
        Assert.Equal(paymentSource.Id, request.PaymentSourceId);
    }

    [Fact]
    public async Task SaveAsync_On400_SurfacesFieldLevelErrorWithoutNavigating()
    {
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            CreateHandler = _ => throw TestApiExceptions.Create(
                System.Net.HttpStatusCode.BadRequest,
                """{"errors":{"CostAmount":["'Cost Amount' must be greater than '0'."]}}"""),
        };
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi);
        viewModel.CustomName = "Netflix";
        viewModel.CostAmount = -5;

        var raised = false;
        viewModel.SaveSucceeded += (_, _) => raised = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.Equal("'Cost Amount' must be greater than '0'.", viewModel.ErrorMessage);
        // The user's entered data must not be lost.
        Assert.Equal("Netflix", viewModel.CustomName);
        Assert.Equal(-5, viewModel.CostAmount);
    }
}
