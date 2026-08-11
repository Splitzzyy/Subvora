using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;
using CommunityToolkit.Mvvm.Messaging;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The add/edit screen used to issue its three GETs strictly in series and say nothing while it
/// did, so the form sat on default values with no indication anything was in flight - and the
/// pickers opened empty dialogs until the fetch happened to land.
/// </summary>
public class DetailPageLoadingTests
{
    private static SubscriptionDetailViewModel CreateViewModel(
        FakeSubscriptionsApi? subscriptionsApi = null,
        FakeCategoriesApi? categoriesApi = null,
        FakePaymentSourcesApi? paymentSourcesApi = null) =>
        new(
            subscriptionsApi ?? new FakeSubscriptionsApi(),
            categoriesApi ?? new FakeCategoriesApi(),
            paymentSourcesApi ?? new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            new WeakReferenceMessenger(),
            new FakeUserPrompt(),
            new FakeConnectivityService());

    [Fact]
    public async Task InitializeAsync_RequestsCategoriesAndPaymentSourcesConcurrently()
    {
        // The regression this guards: both were awaited one after the other, so the second request
        // could not start until the first came back. Each handler blocks until the other has been
        // entered - serial execution deadlocks and the test times out rather than passing slowly.
        var categoriesEntered = new TaskCompletionSource();
        var paymentSourcesEntered = new TaskCompletionSource();

        var categoriesApi = new FakeCategoriesApi
        {
            GetAllHandler = async () =>
            {
                categoriesEntered.SetResult();
                await paymentSourcesEntered.Task;
                return [];
            },
        };
        var paymentSourcesApi = new FakePaymentSourcesApi
        {
            GetAllHandler = async () =>
            {
                paymentSourcesEntered.SetResult();
                await categoriesEntered.Task;
                return [];
            },
        };

        var viewModel = CreateViewModel(categoriesApi: categoriesApi, paymentSourcesApi: paymentSourcesApi);

        await viewModel.InitializeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(categoriesEntered.Task.IsCompletedSuccessfully);
        Assert.True(paymentSourcesEntered.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task InitializeAsync_FetchesTheSubscriptionWithoutWaitingForThePickers()
    {
        // Same shape for the third request: it is independent of the other two, and only *applying*
        // its result depends on the pickers existing.
        var pickersEntered = new TaskCompletionSource();
        var subscriptionEntered = new TaskCompletionSource();
        var id = Guid.NewGuid();

        var categoriesApi = new FakeCategoriesApi
        {
            GetAllHandler = async () =>
            {
                pickersEntered.SetResult();
                await subscriptionEntered.Task;
                return [];
            },
        };
        var subscriptionsApi = new FakeSubscriptionsApi
        {
            GetByIdHandler = async _ =>
            {
                subscriptionEntered.SetResult();
                await pickersEntered.Task;
                return new SubscriptionDto { Id = id, CustomName = "Netflix", Currency = "USD" };
            },
        };

        var viewModel = CreateViewModel(subscriptionsApi: subscriptionsApi, categoriesApi: categoriesApi);
        viewModel.SubscriptionId = id;

        await viewModel.InitializeCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Netflix", viewModel.CustomName);
    }

    [Fact]
    public async Task InitializeAsync_StillResolvesSelectionsAgainstTheLoadedPickers()
    {
        // The one real ordering constraint: the subscription's category/payment source are resolved
        // by id against lists that must already be populated. Fanning the requests out must not
        // break that.
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var paymentSource = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC Card" };
        var id = Guid.NewGuid();

        var viewModel = CreateViewModel(
            subscriptionsApi: new FakeSubscriptionsApi
            {
                GetByIdHandler = _ => Task.FromResult(new SubscriptionDto
                {
                    Id = id,
                    CustomName = "Netflix",
                    Currency = "USD",
                    CategoryId = category.Id,
                    PaymentSourceId = paymentSource.Id,
                }),
            },
            categoriesApi: new FakeCategoriesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([category]) },
            paymentSourcesApi: new FakePaymentSourcesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([paymentSource]) });
        viewModel.SubscriptionId = id;

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(category.Id, viewModel.SelectedCategory?.Id);
        Assert.Equal(paymentSource.Id, viewModel.SelectedPaymentSource?.Id);
    }

    [Fact]
    public async Task InitializeAsync_RaisesAndClearsIsLoading()
    {
        var gate = new TaskCompletionSource();
        var categoriesApi = new FakeCategoriesApi
        {
            GetAllHandler = async () => { await gate.Task; return []; },
        };
        var viewModel = CreateViewModel(categoriesApi: categoriesApi);

        var initialize = viewModel.InitializeCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsLoading);

        gate.SetResult();
        await initialize;

        Assert.False(viewModel.IsLoading);
    }

    [Fact]
    public async Task PickersAreDisabledWhileLoadingAndEnabledAfter()
    {
        var gate = new TaskCompletionSource();
        var categoriesApi = new FakeCategoriesApi
        {
            GetAllHandler = async () => { await gate.Task; return []; },
        };
        var viewModel = CreateViewModel(categoriesApi: categoriesApi);

        var initialize = viewModel.InitializeCommand.ExecuteAsync(null);
        Assert.True(viewModel.ArePickersLoading);

        gate.SetResult();
        await initialize;

        Assert.False(viewModel.ArePickersLoading);
    }

    [Fact]
    public async Task NoPaymentSourcesHint_StaysHiddenWhileLoading()
    {
        // The reported misdirection: keyed on emptiness alone, this told a user who *has* payment
        // sources that they had none and sent them to another tab, every time the screen opened.
        var gate = new TaskCompletionSource();
        var paymentSourcesApi = new FakePaymentSourcesApi
        {
            GetAllHandler = async () =>
            {
                await gate.Task;
                return [new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC Card" }];
            },
        };
        var viewModel = CreateViewModel(paymentSourcesApi: paymentSourcesApi);

        var initialize = viewModel.InitializeCommand.ExecuteAsync(null);
        Assert.False(viewModel.ShowNoPaymentSourcesHint);

        gate.SetResult();
        await initialize;

        // Still false, but now because there genuinely is one.
        Assert.False(viewModel.ShowNoPaymentSourcesHint);
    }

    [Fact]
    public async Task NoPaymentSourcesHint_ShowsOnceTheFetchConfirmsThereAreNone()
    {
        var viewModel = CreateViewModel();

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.True(viewModel.ShowNoPaymentSourcesHint);
    }

    [Fact]
    public async Task OnePickerFailing_StillPopulatesTheOther()
    {
        // Task.WhenAll surfaces one exception and would have discarded the successful half, leaving
        // both pickers empty because one endpoint was down.
        var category = new CategoryDto { Id = Guid.NewGuid(), Name = "Streaming" };
        var viewModel = CreateViewModel(
            categoriesApi: new FakeCategoriesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>([category]) },
            paymentSourcesApi: new FakePaymentSourcesApi { GetAllHandler = () => throw new HttpRequestException("network down") });

        await viewModel.InitializeCommand.ExecuteAsync(null);

        Assert.Equal("Streaming", Assert.Single(viewModel.Categories).Name);
        Assert.Empty(viewModel.PaymentSources);
        Assert.NotNull(viewModel.ErrorMessage);
        // Not left stuck on "Loading..." with both controls dead.
        Assert.False(viewModel.ArePickersLoading);
        Assert.False(viewModel.IsLoading);
    }
}
