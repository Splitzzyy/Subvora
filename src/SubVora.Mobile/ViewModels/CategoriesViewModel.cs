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

    public ObservableCollection<CategoryDto> Categories { get; } = [];

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
            Categories.Add(created);
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
    /// Renames a category the user owns. System defaults are shared by every account, so the server
    /// answers 404 for them - the list marks them and the view hides the action, but the check that
    /// matters is the server's.
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

            // Replaced in place so the row keeps its position - CategoryDto is not observable, so
            // mutating it would not repaint.
            var index = Categories.IndexOf(category);
            if (index >= 0)
            {
                Categories[index] = renamed;
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
