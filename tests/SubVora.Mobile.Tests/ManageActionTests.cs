using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// Rename and Delete used to be reachable only by swiping a row, with the one hint that said so
/// living in the CollectionView's EmptyView - on screen exactly when there was nothing to swipe.
/// Each editable row now carries a visible manage button that opens the same two actions.
/// </summary>
public class ManageActionTests
{
    private static CategoryDto UserCategory(string name = "Music") =>
        new() { Id = Guid.NewGuid(), Name = name, IsSystemDefault = false };

    private static CategoriesViewModel Categories(FakeCategoriesApi api, FakeUserPrompt prompt) =>
        new(api, new FakeConnectivityService(), prompt);

    [Fact]
    public async Task Manage_ChoosingRename_OpensTheRenamePrompt()
    {
        var prompt = new FakeUserPrompt { ActionSheetResult = "Rename", PromptResult = "Podcasts" };
        var api = new FakeCategoriesApi();
        var viewModel = Categories(api, prompt);
        var category = UserCategory();
        viewModel.Categories.Add(category);

        await viewModel.ManageCommand.ExecuteAsync(category);

        var sheet = Assert.Single(prompt.ActionSheetCalls);
        Assert.Equal("Music", sheet.Title);
        Assert.Equal(["Rename", "Delete"], sheet.Actions);
        Assert.Single(prompt.PromptCalls);
        Assert.Equal("Podcasts", Assert.Single(api.RenameCalls).Request.Name);
    }

    [Fact]
    public async Task Manage_ChoosingDelete_StillAsksForConfirmationFirst()
    {
        // The sheet is a route to the action, not a replacement for confirming a destructive one.
        var prompt = new FakeUserPrompt { ActionSheetResult = "Delete", ConfirmResult = true };
        var api = new FakeCategoriesApi();
        var viewModel = Categories(api, prompt);
        var category = UserCategory();
        viewModel.Categories.Add(category);

        await viewModel.ManageCommand.ExecuteAsync(category);

        Assert.Single(prompt.Calls);
        Assert.Equal(category.Id, Assert.Single(api.DeleteCalls));
    }

    [Fact]
    public async Task Manage_Dismissed_DoesNothing()
    {
        // Null is a dismissal. Falling through to a default action here would delete a category the
        // user backed out of.
        var prompt = new FakeUserPrompt { ActionSheetResult = null };
        var api = new FakeCategoriesApi();
        var viewModel = Categories(api, prompt);
        var category = UserCategory();
        viewModel.Categories.Add(category);

        await viewModel.ManageCommand.ExecuteAsync(category);

        Assert.Empty(api.DeleteCalls);
        Assert.Empty(api.RenameCalls);
        Assert.Empty(prompt.PromptCalls);
        Assert.Empty(prompt.Calls);
    }

    [Fact]
    public async Task Manage_OnASystemDefault_NeverEvenOpensTheSheet()
    {
        // The view hides the button on system rows; this is the guard behind it. The server answers
        // 404 for them regardless, but offering the action at all would be a lie.
        var prompt = new FakeUserPrompt { ActionSheetResult = "Delete", ConfirmResult = true };
        var api = new FakeCategoriesApi();
        var viewModel = Categories(api, prompt);
        var system = new CategoryDto { Id = Guid.NewGuid(), Name = "Entertainment", IsSystemDefault = true };
        viewModel.Categories.Add(system);

        await viewModel.ManageCommand.ExecuteAsync(system);

        Assert.Empty(prompt.ActionSheetCalls);
        Assert.Empty(api.DeleteCalls);
    }

    [Fact]
    public async Task PaymentSource_Manage_ChoosingDelete_DeletesAfterConfirming()
    {
        var prompt = new FakeUserPrompt { ActionSheetResult = "Delete", ConfirmResult = true };
        var deleted = new List<Guid>();
        var source = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "HDFC Card", SourceType = PaymentSourceType.Card };
        var api = new FakePaymentSourcesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([source]),
            DeleteHandler = id => { deleted.Add(id); return Task.CompletedTask; },
        };
        var viewModel = new PaymentSourcesViewModel(api, prompt, new FakeConnectivityService());
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.ManageCommand.ExecuteAsync(source);

        Assert.Equal("HDFC Card", Assert.Single(prompt.ActionSheetCalls).Title);
        Assert.Equal(source.Id, Assert.Single(deleted));
        Assert.Empty(viewModel.PaymentSources);
        Assert.False(viewModel.HasPaymentSources);
    }

    [Fact]
    public async Task PaymentSource_HasPaymentSources_TracksTheList()
    {
        // Gates the manage hint. Derived from the collection, so it has to be raised on change -
        // nothing does that automatically.
        var api = new FakePaymentSourcesApi();
        var viewModel = new PaymentSourcesViewModel(api, new FakeUserPrompt(), new FakeConnectivityService());

        Assert.False(viewModel.HasPaymentSources);

        viewModel.NewLabel = "UPI";
        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasPaymentSources);
    }
}
