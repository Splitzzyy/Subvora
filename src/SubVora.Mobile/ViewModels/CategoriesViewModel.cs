using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoriesApi _categoriesApi;
    private readonly IConnectivityService _connectivity;
    private readonly IUserPrompt _userPrompt;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; } = string.Empty;

    /// <summary>
    /// Flat source of truth, in the order the API returned it. Every mutation lands here and then
    /// calls <see cref="RebuildGroups"/> - same split as SubscriptionListViewModel keeps between
    /// its Subscriptions and Groups.
    /// </summary>
    public ObservableCollection<CategoryDto> Categories { get; } = [];

    /// <summary>
    /// What the list actually binds to: the user's own categories first, then the shared system
    /// defaults. Rebuilt from <see cref="Categories"/> rather than mutated alongside it, so a row
    /// can never end up in the wrong section or in two of them.
    /// </summary>
    public ObservableCollection<CategoryGroup> Groups { get; } = [];

    /// <summary>
    /// Whether any row offers rename/delete. Gates the hint under the list - the hint used to live
    /// in the CollectionView's EmptyView, which renders only when there is nothing to act on, so
    /// it was invisible exactly when it applied.
    /// </summary>
    public bool HasManageableCategories => Categories.Any(category => !category.IsSystemDefault);

    public CategoriesViewModel(ICategoriesApi categoriesApi, IConnectivityService connectivity, IUserPrompt userPrompt)
    {
        _categoriesApi = categoriesApi;
        _connectivity = connectivity;
        _userPrompt = userPrompt;
        IsOffline = !connectivity.IsConnected;
    }

    /// <summary>
    /// Whether the device has no network. Refreshed when the screen loads and after a failed write
    /// rather than by subscribing to the connectivity event: these view models are transient while
    /// IConnectivityService is a singleton, so a subscription would outlive the screen.
    /// <para>
    /// Only covers "this phone has no network". A reachable phone talking to a server that is down
    /// still reads as online - that case is caught by the write failing, with a message that says
    /// the change was not saved.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsOffline { get; set; }

    /// <summary>Gates the write button, so an edit that cannot possibly succeed is not offered.</summary>
    public bool CanSubmit => !IsOffline;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        IsOffline = !_connectivity.IsConnected;
        try
        {
            var categories = await _categoriesApi.GetAllAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            RebuildGroups();
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        ErrorMessage = null;
        try
        {
            var created = await _categoriesApi.CreateAsync(new CreateCategoryRequest { Name = NewCategoryName });

            // Appended to the flat list, but RebuildGroups sorts by name - so the row appears where
            // the next load will also put it. Appending straight to a displayed list is what made a
            // new category land at the bottom and then silently relocate on the next visit.
            Categories.Add(created);
            RebuildGroups();

            NewCategoryName = string.Empty;
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            // 409 (duplicate name) isn't in the mapper's table - keep that specific wording.
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ex is ApiException { StatusCode: System.Net.HttpStatusCode.Conflict }
                ? "A category with this name already exists."
                : ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }

    /// <summary>
    /// Rebuilds the two sections from <see cref="Categories"/>. The user's own come first: that is
    /// the half they can act on, and the system block below it is reference material.
    /// <para>
    /// Sorted by name inside each section, matching the server's <c>OrderBy(c =&gt; c.Name)</c>, so a
    /// row never sits in one place now and another after a reload. An empty section is dropped
    /// rather than shown as a bare heading - except that the user section's absence is what the
    /// list's empty-state copy speaks to.
    /// </para>
    /// </summary>
    private void RebuildGroups()
    {
        Groups.Clear();

        // CurrentCultureIgnoreCase, matching how SubscriptionListViewModel orders its group titles.
        // Postgres sorts by the database collation, so the two agree for ASCII names and can differ
        // on accents; the list re-sorting itself on the next load is the cost, and it is small.
        var user = Categories.Where(c => !c.IsSystemDefault)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var system = Categories.Where(c => c.IsSystemDefault)
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (user.Count > 0)
        {
            Groups.Add(new CategoryGroup(CategoryGroup.UserTitle, isSystem: false, user));
        }

        if (system.Count > 0)
        {
            Groups.Add(new CategoryGroup(CategoryGroup.SystemTitle, isSystem: true, system));
        }

        OnPropertyChanged(nameof(HasManageableCategories));
    }

    /// <summary>
    /// The discoverable route to rename/delete, behind the row's manage button. Swipe still works
    /// and is faster, but a swipe is invisible until you happen to try it - which is how this
    /// feature went unnoticed.
    /// </summary>
    [RelayCommand]
    private async Task ManageAsync(CategoryDto category)
    {
        // Belt and braces with the view, which only draws the button on non-system rows. The server
        // answers 404 for a system default either way.
        if (category.IsSystemDefault)
        {
            return;
        }

        var choice = await _userPrompt.ActionSheetAsync(category.Name, "Cancel", "Rename", "Delete");

        switch (choice)
        {
            case "Rename":
                await RenameAsync(category);
                break;
            case "Delete":
                await DeleteAsync(category);
                break;
            // Dismissed, or a string we did not offer. Doing nothing is the only safe default -
            // falling through to Delete on an unrecognised value is how that goes wrong.
        }
    }

    /// <summary>
    /// Renames a category the user owns. System defaults are shared by every account, so the server
    /// answers 404 for them - the sections separate them and the view hides the action, but the
    /// check that matters is the server's.
    /// </summary>
    [RelayCommand]
    private async Task RenameAsync(CategoryDto category)
    {
        var newName = await _userPrompt.PromptAsync("Rename category", "New name", category.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == category.Name)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            var renamed = await _categoriesApi.RenameAsync(category.Id, new CreateCategoryRequest { Name = newName.Trim() });

            // Replaced rather than mutated - CategoryDto is not observable, so mutating it would not
            // repaint. RebuildGroups then re-sorts: a rename that changes the first letter moves the
            // row now, instead of on some later visit.
            var index = Categories.IndexOf(category);
            if (index >= 0)
            {
                Categories[index] = renamed;
                RebuildGroups();
            }
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ex is ApiException { StatusCode: System.Net.HttpStatusCode.Conflict }
                ? "A category with this name already exists."
                : ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }

    /// <summary>
    /// Deletes a category, warning first about what it takes with it. Subscriptions survive -
    /// category_id is ON DELETE SET NULL - but they lose their grouping, and the user should be
    /// told that before it happens rather than discover it on the dashboard.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync(CategoryDto category)
    {
        var confirmed = await _userPrompt.ConfirmAsync(
            "Delete category",
            $"Delete \"{category.Name}\"? Subscriptions using it will stay, but become uncategorised.",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            var result = await _categoriesApi.DeleteAsync(category.Id);
            Categories.Remove(category);
            RebuildGroups();

            if (result.SubscriptionsUncategorized > 0)
            {
                var plural = result.SubscriptionsUncategorized == 1 ? "subscription is" : "subscriptions are";
                await _userPrompt.AlertAsync(
                    "Category deleted",
                    $"{result.SubscriptionsUncategorized} {plural} now uncategorised.");
            }
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }
}
