using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// What the detail screen does when the server refuses a save because the record moved on - most
/// often the charge being marked paid on another device while this screen was open.
/// </summary>
public class SaveConflictTests
{
    private const uint LoadedVersion = 42u;
    private const uint ServerVersion = 99u;

    private static SubscriptionDto StoredSubscription(uint version, string name = "Netflix", DateOnly? nextBillingDate = null) => new()
    {
        Id = Guid.NewGuid(),
        CustomName = name,
        CostAmount = 19.99m,
        Currency = "INR",
        CycleCadence = BillingCycleType.Monthly,
        PurchaseDate = new DateOnly(2026, 1, 1),
        NextBillingDate = nextBillingDate ?? new DateOnly(2026, 8, 1),
        AlertDaysAdvance = 3,
        IsActive = true,
        Version = version,
    };

    private static SubscriptionDetailViewModel CreateViewModel(FakeSubscriptionsApi subscriptionsApi, IMessenger? messenger = null) =>
        new(
            subscriptionsApi,
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            messenger ?? new WeakReferenceMessenger(),
            new FakeUserPrompt());

    private static async Task<SubscriptionDetailViewModel> LoadedInEditModeAsync(FakeSubscriptionsApi api, IMessenger? messenger = null)
    {
        var viewModel = CreateViewModel(api, messenger);
        viewModel.SubscriptionId = Guid.NewGuid();
        // Initialize, not LoadPickers - the latter only fills the dropdowns. Loading the record is
        // what captures the version this screen is editing against.
        await viewModel.InitializeCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task SaveAsync_InEditMode_SendsTheVersionItLoaded()
    {
        CreateSubscriptionRequest? sent = null;
        var api = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(StoredSubscription(LoadedVersion)),
            UpdateHandler = (_, request) =>
            {
                sent = request;
                return Task.FromResult(StoredSubscription(ServerVersion));
            },
        };
        var viewModel = await LoadedInEditModeAsync(api);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal(LoadedVersion, sent!.Version);
    }

    [Fact]
    public async Task SaveAsync_OnCreate_SendsNoVersion()
    {
        // Nothing to conflict with on a record that does not exist yet.
        CreateSubscriptionRequest? sent = null;
        var api = new FakeSubscriptionsApi
        {
            CreateHandler = request =>
            {
                sent = request;
                return Task.FromResult(StoredSubscription(ServerVersion));
            },
        };
        var viewModel = CreateViewModel(api);
        viewModel.CustomName = "Spotify";
        viewModel.CostAmount = 9.99m;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Null(sent!.Version);
    }

    [Fact]
    public async Task SaveAsync_On409_ReloadsTheRecordAndExplainsWithoutNavigatingAway()
    {
        var serverState = StoredSubscription(LoadedVersion);
        var api = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(serverState),
            UpdateHandler = (_, _) => throw TestApiExceptions.Create(HttpStatusCode.Conflict),
        };
        var viewModel = await LoadedInEditModeAsync(api);

        // Someone marks the charge paid elsewhere: the billing date advances and the version moves.
        serverState = StoredSubscription(ServerVersion, name: "Netflix", nextBillingDate: new DateOnly(2026, 9, 1));

        var saved = false;
        viewModel.SaveSucceeded += (_, _) => saved = true;

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(saved);
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.Contains("changed somewhere else", viewModel.ErrorMessage);

        // Reloaded, so the form now shows the current truth rather than the state the edit assumed.
        Assert.Equal(new DateTime(2026, 9, 1), viewModel.NextBillingDate);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SaveAsync_On409_DoesNotAnnounceAChangeThatDidNotHappen()
    {
        // The burn-rate banner listens for this. A refused save moved nothing, so publishing it
        // would make the headline figure refetch for no reason - and imply the edit landed.
        var api = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(StoredSubscription(ServerVersion)),
            UpdateHandler = (_, _) => throw TestApiExceptions.Create(HttpStatusCode.Conflict),
        };
        var messenger = new WeakReferenceMessenger();
        var announced = false;
        messenger.Register<SubscriptionsChangedMessage>(new object(), (_, _) => announced = true);

        var viewModel = await LoadedInEditModeAsync(api, messenger);

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.False(announced);
    }

    [Fact]
    public async Task SaveAsync_On409_SendsTheRefreshedVersionOnTheNextAttempt()
    {
        // The point of reloading: retrying with the same stale version would conflict forever.
        var versions = new List<uint?>();
        var conflictNext = true;
        var api = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(StoredSubscription(conflictNext ? LoadedVersion : ServerVersion)),
        };
        api.UpdateHandler = (_, request) =>
        {
            versions.Add(request.Version);
            if (conflictNext)
            {
                conflictNext = false;
                throw TestApiExceptions.Create(HttpStatusCode.Conflict);
            }

            return Task.FromResult(StoredSubscription(ServerVersion));
        };

        var viewModel = await LoadedInEditModeAsync(api);

        await viewModel.SaveCommand.ExecuteAsync(null);
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal([LoadedVersion, ServerVersion], versions);
    }

    [Fact]
    public async Task SaveAsync_On409_WhenTheReloadAlsoFails_KeepsTheReloadsErrorRatherThanClaimingAReload()
    {
        var api = new FakeSubscriptionsApi
        {
            GetByIdHandler = _ => Task.FromResult(StoredSubscription(LoadedVersion)),
            UpdateHandler = (_, _) => throw TestApiExceptions.Create(HttpStatusCode.Conflict),
        };
        var viewModel = await LoadedInEditModeAsync(api);

        // Connection drops between the refused save and the reload.
        api.GetByIdHandler = _ => throw new HttpRequestException("Connection refused");

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("You appear to be offline.", viewModel.ErrorMessage);
    }
}
