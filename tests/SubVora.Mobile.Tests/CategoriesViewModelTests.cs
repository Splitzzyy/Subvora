using System.Net;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

public class CategoriesViewModelTests
{
    [Fact]
    public async Task LoadAsync_PopulatesListDistinguishingSystemFromUserOwned()
    {
        var api = new FakeCategoriesApi
        {
            GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>(
            [
                new CategoryDto { Id = Guid.NewGuid(), Name = "Entertainment", IsSystemDefault = true },
                new CategoryDto { Id = Guid.NewGuid(), Name = "My Custom Category", IsSystemDefault = false },
            ]),
        };
        var viewModel = new CategoriesViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Categories.Count);
        Assert.True(viewModel.Categories[0].IsSystemDefault);
        Assert.False(viewModel.Categories[1].IsSystemDefault);
    }

    [Fact]
    public async Task AddAsync_WithValidName_CallsCreateAndAppendsToList()
    {
        var api = new FakeCategoriesApi();
        var viewModel = new CategoriesViewModel(api) { NewCategoryName = "Streaming" };

        await viewModel.AddCommand.ExecuteAsync(null);

        var category = Assert.Single(viewModel.Categories);
        Assert.Equal("Streaming", category.Name);
        Assert.Equal(string.Empty, viewModel.NewCategoryName);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_WithDuplicateName_SurfacesApiError()
    {
        var api = new FakeCategoriesApi
        {
            CreateHandler = _ => throw TestApiExceptions.Create(HttpStatusCode.Conflict),
        };
        var viewModel = new CategoriesViewModel(api) { NewCategoryName = "Utilities" };

        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Categories);
        Assert.Equal("A category with this name already exists.", viewModel.ErrorMessage);
    }

    [Fact]
    public async Task AddAsync_WithEmptyName_SurfacesApiValidationError()
    {
        var api = new FakeCategoriesApi
        {
            CreateHandler = _ => throw TestApiExceptions.Create(
                HttpStatusCode.BadRequest,
                """{"errors":{"Name":["'Name' must not be empty."]}}"""),
        };
        var viewModel = new CategoriesViewModel(api) { NewCategoryName = "" };

        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Categories);
        Assert.Equal("'Name' must not be empty.", viewModel.ErrorMessage);
    }
}
