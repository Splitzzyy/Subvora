using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Deleting from the detail screen. The list's swipe-to-delete is undiscoverable on its own, so this
/// is the path most users will take - it has to confirm first and it has to leave the list correct.
/// </summary>
public class SubscriptionDeleteTests
{
    private static SubscriptionDetailViewModel CreateViewModel(
        FakeSubscriptionsApi subscriptionsApi,
        FakeUserPrompt userPrompt,
        IMessenger? messenger = null) =>
        new(
            subscriptionsApi,
            new FakeCategoriesApi(),
            new FakePaymentSourcesApi(),
            new FakeDebouncer(),
            messenger ?? new WeakReferenceMessenger(),
            userPrompt);

    private static FakeSubscriptionsApi ApiReturning(Guid id) => new()
    {
        GetByIdHandler = _ => Task.FromResult(new SubscriptionDto { Id = id, CustomName = "Netflix", Currency = "INR" }),
    };

    [Fact]
    public async Task Deleting_AsksForConfirmationFirst()
    {
        var id = Guid.NewGuid();
        var userPrompt = new FakeUserPrompt { ConfirmResult = false };
        var api = ApiReturning(id);
        var viewModel = CreateViewModel(api, userPrompt);
        viewModel.SubscriptionId = id;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(api.DeleteCalls);
    }

    [Fact]
    public async Task Deleting_WhenConfirmed_CallsTheApiAndRaisesDeleted()
    {
        var id = Guid.NewGuid();
        var api = ApiReturning(id);
        var viewModel = CreateViewModel(api, new FakeUserPrompt { ConfirmResult = true });
        viewModel.SubscriptionId = id;

        var raised = false;
        viewModel.Deleted += (_, _) => raised = true;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal([id], api.DeleteCalls);
        Assert.True(raised);
    }

    [Fact]
    public async Task Deleting_PublishesSubscriptionsChangedSoTheDashboardTotalMoves()
    {
        var id = Guid.NewGuid();
        var messenger = new WeakReferenceMessenger();
        var received = 0;
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => received++);

        var viewModel = CreateViewModel(ApiReturning(id), new FakeUserPrompt { ConfirmResult = true }, messenger);
        viewModel.SubscriptionId = id;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(1, received);
    }

    [Fact]
    public async Task Deleting_InAddMode_DoesNothing()
    {
        var api = new FakeSubscriptionsApi();
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(api, userPrompt);

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(api.DeleteCalls);
        // Nothing to delete means nothing to ask about either.
        Assert.Empty(userPrompt.Calls);
    }

    [Fact]
    public async Task Deleting_SomethingAlreadyGone_IsTreatedAsSuccess()
    {
        var id = Guid.NewGuid();
        var api = ApiReturning(id);
        api.DeleteHandler = _ => throw TestApiExceptions.Create(HttpStatusCode.NotFound);

        var viewModel = CreateViewModel(api, new FakeUserPrompt { ConfirmResult = true });
        viewModel.SubscriptionId = id;

        var raised = false;
        viewModel.Deleted += (_, _) => raised = true;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        // The user asked for it to be gone and it is gone; stranding them on a dead record instead
        // would be worse than the redundant delete they just performed.
        Assert.True(raised);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Deleting_WhenTheServerFails_KeepsTheUserOnThePageWithAnError()
    {
        var id = Guid.NewGuid();
        var api = ApiReturning(id);
        api.DeleteHandler = _ => throw TestApiExceptions.Create(HttpStatusCode.InternalServerError);

        var viewModel = CreateViewModel(api, new FakeUserPrompt { ConfirmResult = true });
        viewModel.SubscriptionId = id;

        var raised = false;
        viewModel.Deleted += (_, _) => raised = true;

        await viewModel.DeleteCommand.ExecuteAsync(null);

        Assert.False(raised);
        Assert.NotNull(viewModel.ErrorMessage);
        Assert.False(viewModel.IsBusy);
    }
}
