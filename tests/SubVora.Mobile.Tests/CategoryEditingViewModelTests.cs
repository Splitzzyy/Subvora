using System.Net;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class CategoryEditingViewModelTests
{
    private static CategoryDto Category(string name, bool isSystemDefault = false) =>
        new() { Id = Guid.NewGuid(), Name = name, IsSystemDefault = isSystemDefault };

    private static async Task<CategoriesViewModel> LoadedAsync(FakeCategoriesApi api, FakeUserPrompt prompt, params CategoryDto[] categories)
    {
        api.GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>(categories);
        var viewModel = new CategoriesViewModel(api, new FakeConnectivityService(), prompt, new WeakReferenceMessenger());
        await viewModel.LoadCommand.ExecuteAsync(null);
        return viewModel;
    }

    [Fact]
    public async Task Rename_ReplacesTheRowInPlace()
    {
        // CategoryDto is not observable, so mutating it would not repaint - the row has to be
        // swapped, and swapped at its own index so it does not jump to the bottom.
        var api = new FakeCategoriesApi();
        var prompt = new FakeUserPrompt { PromptResult = "Entertainment" };
        var first = Category("Aardvarks");
        var target = Category("Entertainmnet");
        var viewModel = await LoadedAsync(api, prompt, first, target);

        await viewModel.RenameCommand.ExecuteAsync(target);

        Assert.Equal(2, viewModel.Categories.Count);
        Assert.Equal("Entertainment", viewModel.Categories[1].Name);
        Assert.Equal(target.Id, api.RenameCalls.Single().Id);
    }

    [Fact]
    public async Task Rename_PrefillsThePromptWithTheCurrentName()
    {
        var prompt = new FakeUserPrompt { PromptResult = "Whatever" };
        var target = Category("Streaming");
        var viewModel = await LoadedAsync(new FakeCategoriesApi(), prompt, target);

        await viewModel.RenameCommand.ExecuteAsync(target);

        Assert.Equal("Streaming", prompt.PromptCalls.Single().InitialValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_WhenCancelledOrBlank_DoesNothing(string? promptResult)
    {
        // Null is a cancel, not a request to clear the name - and an empty box is not a rename
        // either. Neither should reach the server.
        var api = new FakeCategoriesApi();
        var prompt = new FakeUserPrompt { PromptResult = promptResult };
        var target = Category("Streaming");
        var viewModel = await LoadedAsync(api, prompt, target);

        await viewModel.RenameCommand.ExecuteAsync(target);

        Assert.Empty(api.RenameCalls);
        Assert.Equal("Streaming", viewModel.Categories.Single().Name);
    }

    [Fact]
    public async Task Rename_ToTheSameName_DoesNotCallTheServer()
    {
        var api = new FakeCategoriesApi();
        var prompt = new FakeUserPrompt { PromptResult = "Streaming" };
        var viewModel = await LoadedAsync(api, prompt, Category("Streaming"));

        await viewModel.RenameCommand.ExecuteAsync(viewModel.Categories.Single());

        Assert.Empty(api.RenameCalls);
    }

    [Fact]
    public async Task Rename_ToADuplicate_KeepsTheSpecificConflictWording()
    {
        var api = new FakeCategoriesApi
        {
            RenameHandler = (_, _) => throw TestApiExceptions.Create(HttpStatusCode.Conflict),
        };
        var prompt = new FakeUserPrompt { PromptResult = "Taken" };
        var viewModel = await LoadedAsync(api, prompt, Category("Streaming"));

        await viewModel.RenameCommand.ExecuteAsync(viewModel.Categories.Single());

        Assert.Equal("A category with this name already exists.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task Delete_WarnsThatSubscriptionsSurviveButLoseTheirGrouping()
    {
        var prompt = new FakeUserPrompt { ConfirmResult = false };
        var target = Category("Doomed");
        var viewModel = await LoadedAsync(new FakeCategoriesApi(), prompt, target);

        await viewModel.DeleteCommand.ExecuteAsync(target);

        var (title, message) = prompt.Calls.Single();
        Assert.Equal("Delete category", title);
        Assert.Contains("uncategorised", message);
    }

    [Fact]
    public async Task Delete_WhenNotConfirmed_KeepsTheCategory()
    {
        var api = new FakeCategoriesApi();
        var prompt = new FakeUserPrompt { ConfirmResult = false };
        var target = Category("Doomed");
        var viewModel = await LoadedAsync(api, prompt, target);

        await viewModel.DeleteCommand.ExecuteAsync(target);

        Assert.Empty(api.DeleteCalls);
        Assert.Single(viewModel.Categories);
    }

    [Fact]
    public async Task Delete_ReportsHowManySubscriptionsLostTheirCategory()
    {
        // The user is entitled to know what moved. Without this the change only shows up later, as
        // an unexplained "Uncategorized" row on the dashboard.
        var api = new FakeCategoriesApi
        {
            DeleteHandler = _ => Task.FromResult(new DeleteCategoryResult { SubscriptionsUncategorized = 3 }),
        };
        var prompt = new FakeUserPrompt { ConfirmResult = true };
        var target = Category("Doomed");
        var viewModel = await LoadedAsync(api, prompt, target);

        await viewModel.DeleteCommand.ExecuteAsync(target);

        Assert.Empty(viewModel.Categories);
        Assert.Contains("3 subscriptions are now uncategorised", prompt.AlertCalls.Single().Message);
    }

    [Fact]
    public async Task Delete_WithNothingAffected_SaysNothingExtra()
    {
        var prompt = new FakeUserPrompt { ConfirmResult = true };
        var target = Category("Unused");
        var viewModel = await LoadedAsync(new FakeCategoriesApi(), prompt, target);

        await viewModel.DeleteCommand.ExecuteAsync(target);

        Assert.Empty(viewModel.Categories);
        Assert.Empty(prompt.AlertCalls);
    }

    [Fact]
    public async Task Delete_WhenOffline_SaysTheChangeDidNotLand()
    {
        var api = new FakeCategoriesApi { DeleteHandler = _ => throw new HttpRequestException("Connection refused") };
        var prompt = new FakeUserPrompt { ConfirmResult = true };
        var target = Category("Doomed");
        var viewModel = await LoadedAsync(api, prompt, target);

        await viewModel.DeleteCommand.ExecuteAsync(target);

        Assert.Equal("You're offline — this change wasn't saved. Try again once you're connected.", viewModel.ErrorMessage);
        // Still listed, because it still exists.
        Assert.Single(viewModel.Categories);
    }

    [Fact]
    public async Task RenamePaymentSource_KeepsItsTypeAndReplacesTheRow()
    {
        // Rename means rename. The type is chosen when the source is created, and quietly resetting
        // it here would change which icon and tint the row carries.
        var api = new FakePaymentSourcesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>(
                [new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC 4021", SourceType = PaymentSourceType.Card }]),
        };
        var prompt = new FakeUserPrompt { PromptResult = "HDFC Credit ••4021" };
        var viewModel = new PaymentSourcesViewModel(api, prompt, new FakeConnectivityService(), new WeakReferenceMessenger());
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.RenameCommand.ExecuteAsync(viewModel.PaymentSources.Single());

        Assert.Equal("HDFC Credit ••4021", viewModel.PaymentSources.Single().Label);
        Assert.Equal(PaymentSourceType.Card, api.UpdateCalls.Single().Request.SourceType);
    }
}
