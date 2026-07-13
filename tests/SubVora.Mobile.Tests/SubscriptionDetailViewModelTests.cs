using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class SubscriptionDetailViewModelTests
{
    private static SubscriptionDetailViewModel CreateViewModel(
        FakeSubscriptionsApi? subscriptionsApi = null,
        FakeCategoriesApi? categoriesApi = null,
        FakePaymentSourcesApi? paymentSourcesApi = null,
        FakeDebouncer? debouncer = null) =>
        new(
            subscriptionsApi ?? new FakeSubscriptionsApi(),
            categoriesApi ?? new FakeCategoriesApi(),
            paymentSourcesApi ?? new FakePaymentSourcesApi(),
            debouncer ?? new FakeDebouncer());

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

    [Fact]
    public void TypingFewerThanThreeCharacters_NeverCallsResolve()
    {
        var subscriptionsApi = new FakeSubscriptionsApi();
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);

        viewModel.CustomName = "Ne";
        debouncer.Flush();

        Assert.Empty(subscriptionsApi.ResolveCalls);
        Assert.Equal(0, debouncer.DebounceCallCount);
    }

    [Fact]
    public void RapidKeystrokesWithinDebounceWindow_OnlyResolvesOnceAfterThePause()
    {
        var subscriptionsApi = new FakeSubscriptionsApi();
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);

        foreach (var partial in new[] { "Net", "Netf", "Netfl", "Netfli", "Netflix" })
        {
            viewModel.CustomName = partial;
        }

        Assert.Empty(subscriptionsApi.ResolveCalls);

        debouncer.Flush();

        var call = Assert.Single(subscriptionsApi.ResolveCalls);
        Assert.Equal("Netflix", call.Input);
    }

    [Fact]
    public void AutoFillResponse_PreFillsNameCategoryAndLogo()
    {
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            ResolveHandler = _ => Task.FromResult(new ResolveSubscriptionResponse
            {
                Tier = MatchConfidenceTier.AutoFill,
                ProviderName = "Netflix",
                LogoUrl = "https://example.com/netflix.png",
                CategoryId = category.Id,
            }),
        };
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);
        viewModel.Categories.Add(category);

        viewModel.CustomName = "Netflix";
        debouncer.Flush();

        Assert.Equal("Netflix", viewModel.CustomName);
        Assert.Equal("https://example.com/netflix.png", viewModel.SuggestedLogoUrl);
        Assert.Equal(category, viewModel.SelectedCategory);
        Assert.Null(viewModel.SuggestedTier);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void SuggestConfirmResponse_ShowsPromptWithoutAutoFillingUntilAccepted()
    {
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            ResolveHandler = _ => Task.FromResult(new ResolveSubscriptionResponse
            {
                Tier = MatchConfidenceTier.SuggestConfirm,
                ProviderName = "Netflix",
                LogoUrl = "https://example.com/netflix.png",
                CategoryId = category.Id,
            }),
        };
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);
        viewModel.Categories.Add(category);

        viewModel.CustomName = "nflx";
        debouncer.Flush();

        Assert.Equal(MatchConfidenceTier.SuggestConfirm, viewModel.SuggestedTier);
        Assert.Equal("Netflix", viewModel.SuggestedProviderName);
        Assert.Equal("nflx", viewModel.CustomName);
        Assert.Null(viewModel.SelectedCategory);

        viewModel.AcceptSuggestionCommand.Execute(null);

        Assert.Equal("Netflix", viewModel.CustomName);
        Assert.Equal(category, viewModel.SelectedCategory);
        Assert.Null(viewModel.SuggestedTier);
    }

    [Fact]
    public void ManualResponse_LeavesFormAsFreeEntryWithNoErrorShown()
    {
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            ResolveHandler = _ => Task.FromResult(new ResolveSubscriptionResponse { Tier = MatchConfidenceTier.Manual }),
        };
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);

        viewModel.CustomName = "Some Obscure Service";
        debouncer.Flush();

        Assert.Equal("Some Obscure Service", viewModel.CustomName);
        Assert.Null(viewModel.SuggestedTier);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public void RateLimitedResolveResponse_DegradesSilentlyWithoutError()
    {
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            ResolveHandler = _ => throw TestApiExceptions.Create(System.Net.HttpStatusCode.TooManyRequests),
        };
        var debouncer = new FakeDebouncer();
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, debouncer: debouncer);

        viewModel.CustomName = "Netflix";
        debouncer.Flush();

        Assert.Null(viewModel.ErrorMessage);
        Assert.Null(viewModel.SuggestedTier);
    }

    [Fact]
    public async Task InitializeAsync_WithExistingId_LoadsAndPreFillsFormViaGetById()
    {
        var subscriptionId = Guid.NewGuid();
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var paymentSource = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa" };
        var categoriesApi = new FakeCategoriesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([category]) };
        var paymentSourcesApi = new FakePaymentSourcesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([paymentSource]) };
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(new SubscriptionDto
            {
                Id = subscriptionId,
                CustomName = "Netflix",
                CostAmount = 15.99m,
                Currency = "USD",
                CycleCadence = BillingCycleType.Monthly,
                PurchaseDate = new DateOnly(2026, 1, 1),
                NextBillingDate = new DateOnly(2026, 8, 1),
                AlertDaysAdvance = 3,
                CategoryId = category.Id,
                PaymentSourceId = paymentSource.Id,
                IsFreeTrial = false,
                IsActive = true,
            }),
        };
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, categoriesApi: categoriesApi, paymentSourcesApi: paymentSourcesApi);
        viewModel.SubscriptionId = subscriptionId;

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsEditMode);
        Assert.Equal("Edit Subscription", viewModel.PageTitle);
        Assert.Equal("Netflix", viewModel.CustomName);
        Assert.Equal(15.99m, viewModel.CostAmount);
        Assert.Equal(category, viewModel.SelectedCategory);
        Assert.Equal(paymentSource, viewModel.SelectedPaymentSource);
    }

    [Fact]
    public async Task SaveAsync_InEditMode_CallsUpdateNotCreateAndRaisesSaveSucceeded()
    {
        var subscriptionId = Guid.NewGuid();
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(new SubscriptionDto { Id = subscriptionId, CustomName = "Netflix", Currency = "USD" }),
            UpdateHandler = (_, request) => Task.FromResult(new SubscriptionDto { Id = subscriptionId, CustomName = request.CustomName, Currency = request.Currency }),
        };
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi);
        viewModel.SubscriptionId = subscriptionId;
        await viewModel.InitializeCommand.ExecuteAsync(null);
        viewModel.CostAmount = 19.99m;

        var raised = false;
        viewModel.SaveSucceeded += (_, _) => raised = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Empty(subscriptionsApi.CreateCalls);
    }

    [Fact]
    public async Task InitializeAsync_On404_RaisesSubscriptionNotFound()
    {
        var subscriptionId = Guid.NewGuid();
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => throw TestApiExceptions.Create(System.Net.HttpStatusCode.NotFound),
        };
        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi);
        viewModel.SubscriptionId = subscriptionId;

        var raised = false;
        viewModel.SubscriptionNotFound += (_, _) => raised = true;

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
    }
}
