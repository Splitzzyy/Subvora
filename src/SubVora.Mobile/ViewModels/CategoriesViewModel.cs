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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string NewCategoryName { get; set; } = string.Empty;

    public ObservableCollection<CategoryDto> Categories { get; } = [];

    public CategoriesViewModel(ICategoriesApi categoriesApi)
    {
        _categoriesApi = categoriesApi;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var categories = await _categoriesApi.GetAllAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }
        catch (ApiException ex)
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
        catch (ApiException ex)
        {
            // 409 (duplicate name) isn't in the mapper's table - keep that specific wording.
            ErrorMessage = ex.StatusCode == System.Net.HttpStatusCode.Conflict
                ? "A category with this name already exists."
                : ApiErrorMapper.ToDisplayMessage(ex);
        }
    }
}
