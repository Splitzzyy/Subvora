using System.Net;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class PaymentSourcesViewModelTests
{
    private static PaymentSourcesViewModel CreateViewModel(
        FakePaymentSourcesApi? api = null,
        FakeUserPrompt? userPrompt = null) =>
        new(api ?? new FakePaymentSourcesApi(), userPrompt ?? new FakeUserPrompt());

    [Fact]
    public async Task LoadAsync_PopulatesList()
    {
        var api = new FakePaymentSourcesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>(
                [new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa", SourceType = PaymentSourceType.Card }]),
        };
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var source = Assert.Single(viewModel.PaymentSources);
        Assert.Equal("Visa", source.Label);
        Assert.Equal(PaymentSourceType.Card, source.SourceType);
    }

    [Fact]
    public async Task AddAsync_WithValidEntry_CallsCreateAndAppendsToList()
    {
        var api = new FakePaymentSourcesApi();
        var viewModel = CreateViewModel(api);
        viewModel.NewLabel = "Chase Checking";
        viewModel.NewSourceType = PaymentSourceType.BankAccount;

        await viewModel.AddCommand.ExecuteAsync(null);

        var source = Assert.Single(viewModel.PaymentSources);
        Assert.Equal("Chase Checking", source.Label);
        Assert.Equal(PaymentSourceType.BankAccount, source.SourceType);
        Assert.Equal(string.Empty, viewModel.NewLabel);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_WithEmptyLabel_SurfacesApiValidationError()
    {
        var api = new FakePaymentSourcesApi
        {
            CreateHandler = _ => throw TestApiExceptions.Create(
                HttpStatusCode.BadRequest,
                """{"errors":{"Label":["'Label' must not be empty."]}}"""),
        };
        var viewModel = CreateViewModel(api);
        viewModel.NewLabel = "";

        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.PaymentSources);
        Assert.Equal("'Label' must not be empty.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task DeleteAsync_WhenConfirmed_CallsDeleteAndRemovesFromList()
    {
        var source = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa", SourceType = PaymentSourceType.Card };
        var api = new FakePaymentSourcesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([source]) };
        var userPrompt = new FakeUserPrompt { ConfirmResult = true };
        var viewModel = CreateViewModel(api, userPrompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteCommand.ExecuteAsync(source.Id);

        Assert.Empty(viewModel.PaymentSources);
    }

    [Fact]
    public async Task DeleteAsync_WhenDeclined_MakesNoApiCallAndLeavesItemInPlace()
    {
        var source = new PaymentSourceDto { Id = Guid.NewGuid(), Label = "Visa", SourceType = PaymentSourceType.Card };
        var api = new FakePaymentSourcesApi { GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([source]) };
        var userPrompt = new FakeUserPrompt { ConfirmResult = false };
        var viewModel = CreateViewModel(api, userPrompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteCommand.ExecuteAsync(source.Id);

        Assert.Single(viewModel.PaymentSources);
    }
}
