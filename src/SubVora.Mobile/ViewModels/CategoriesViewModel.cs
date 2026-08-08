using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Services;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class CategoriesViewModel : ObservableObject
{
    private readonly ICategoriesApi _categoriesApi;
    private readonly IConnectivityService _connectivity;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; } = string.Empty;

    public ObservableCollection<CategoryDto> Categories { get; } = [];

    public CategoriesViewModel(ICategoriesApi categoriesApi, IConnectivityService connectivity)
    {
        _categoriesApi = categoriesApi;
        _connectivity = connectivity;
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
}
