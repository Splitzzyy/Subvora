using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Tests.Fakes;
using SubVora.Mobile.ViewModels;

namespace SubVora.Mobile.Tests;

/// <summary>
/// The Categories list is two sections, not one: the seeded defaults every account shares, and the
/// user's own - which are the only ones that can be renamed or deleted. Flat, the two were
/// indistinguishable, and a newly added row landed at the bottom and then silently relocated.
/// </summary>
public class CategoryGroupingTests
{
    private static CategoryDto System(string name) => new() { Id = Guid.NewGuid(), Name = name, IsSystemDefault = true };

    private static CategoryDto User(string name) => new() { Id = Guid.NewGuid(), Name = name, IsSystemDefault = false };

    private static CategoriesViewModel CreateViewModel(FakeCategoriesApi api, FakeUserPrompt? prompt = null) =>
        new(api, new FakeConnectivityService(), prompt ?? new FakeUserPrompt());

    private static FakeCategoriesApi ApiReturning(params CategoryDto[] categories) => new()
    {
        GetAllHandler = () => Task.FromResult<IReadOnlyList<CategoryDto>>(categories),
    };

    [Fact]
    public async Task LoadAsync_SplitsIntoUserThenSystem()
    {
        var api = ApiReturning(System("Entertainment"), User("Music"), System("Utilities"));
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Groups.Count);

        // The user's own first: that is the half they can act on, and the system block below it is
        // reference material.
        Assert.Equal(CategoryGroup.UserTitle, viewModel.Groups[0].Title);
        Assert.False(viewModel.Groups[0].IsSystem);
        Assert.Equal(["Music"], viewModel.Groups[0].Select(c => c.Name));

        Assert.Equal(CategoryGroup.SystemTitle, viewModel.Groups[1].Title);
        Assert.True(viewModel.Groups[1].IsSystem);
        Assert.Equal(["Entertainment", "Utilities"], viewModel.Groups[1].Select(c => c.Name));
    }

    [Fact]
    public async Task LoadAsync_WithNoUserCategories_ShowsOnlyTheSystemSection()
    {
        // A bare heading over nothing is worse than no heading - the list's empty-state copy is what
        // speaks to having created none.
        var api = ApiReturning(System("Entertainment"), System("Utilities"));
        var viewModel = CreateViewModel(api);

        await viewModel.LoadCommand.ExecuteAsync(null);

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(CategoryGroup.SystemTitle, group.Title);
        Assert.False(viewModel.HasManageableCategories);
    }

    [Fact]
    public async Task AddAsync_PlacesTheNewCategoryInSortedPosition_NotAtTheBottom()
    {
        // The reported bug: it appeared last, then jumped on the next visit because the server
        // returns everything ordered by name.
        var api = ApiReturning(User("Alpha"), User("Zulu"), System("Entertainment"));
        var viewModel = CreateViewModel(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.NewCategoryName = "Music";
        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Equal(["Alpha", "Music", "Zulu"], viewModel.Groups[0].Select(c => c.Name));
    }

    [Fact]
    public async Task AddAsync_PutsTheNewCategoryInTheUserSection_NotTheSystemOne()
    {
        var api = ApiReturning(System("Entertainment"));
        var viewModel = CreateViewModel(api);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.NewCategoryName = "Music";
        await viewModel.AddCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Groups.Count);
        Assert.Equal(CategoryGroup.UserTitle, viewModel.Groups[0].Title);
        Assert.Equal("Music", Assert.Single(viewModel.Groups[0]).Name);
        Assert.True(viewModel.HasManageableCategories);
    }

    [Fact]
    public async Task RenameAsync_ResortsImmediately_RatherThanOnTheNextLoad()
    {
        var api = ApiReturning(User("Alpha"), User("Music"), User("Zulu"));
        var prompt = new FakeUserPrompt { PromptResult = "Nova" };
        var viewModel = CreateViewModel(api, prompt);
        await viewModel.LoadCommand.ExecuteAsync(null);

        // "Alpha" -> "Nova" moves it between Music and Zulu.
        await viewModel.RenameCommand.ExecuteAsync(viewModel.Groups[0].Single(c => c.Name == "Alpha"));

        Assert.Equal(["Music", "Nova", "Zulu"], viewModel.Groups[0].Select(c => c.Name));
    }

    [Fact]
    public async Task DeleteAsync_DropsTheUserSectionOnceItsLastRowIsGone()
    {
        var api = ApiReturning(User("Music"), System("Entertainment"));
        var viewModel = CreateViewModel(api, new FakeUserPrompt { ConfirmResult = true });
        await viewModel.LoadCommand.ExecuteAsync(null);

        await viewModel.DeleteCommand.ExecuteAsync(viewModel.Groups[0].Single());

        var group = Assert.Single(viewModel.Groups);
        Assert.Equal(CategoryGroup.SystemTitle, group.Title);
        Assert.False(viewModel.HasManageableCategories);
    }

    [Fact]
    public void Summary_PluralisesTheCount()
    {
        Assert.Equal("1 category", new CategoryGroup("x", false, [User("A")]).Summary);
        Assert.Equal("2 categories", new CategoryGroup("x", false, [User("A"), User("B")]).Summary);
    }
}
