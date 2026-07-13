using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardApi _dashboardApi;
    private readonly ILocalCacheService _localCacheService;

    [ObservableProperty]
    public partial decimal Weekly { get; set; }

    [ObservableProperty]
    public partial decimal Monthly { get; set; }

    [ObservableProperty]
    public partial decimal Yearly { get; set; }

    [ObservableProperty]
    public partial decimal OneTimeThisYear { get; set; }

    [ObservableProperty]
    public partial string HomeCurrency { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsShowingCachedData { get; set; }

    public ObservableCollection<CategoryBreakdownItem> ByCategory { get; } = [];

    public DashboardViewModel(IDashboardApi dashboardApi, ILocalCacheService localCacheService)
    {
        _dashboardApi = dashboardApi;
        _localCacheService = localCacheService;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _dashboardApi.GetBurnRateAsync();
            ApplyBurnRate(result.Weekly, result.Monthly, result.Yearly, result.OneTimeThisYear, result.HomeCurrency, result.ByCategory);
            IsShowingCachedData = false;

            await _localCacheService.UpsertAsync(new CachedBurnRate
            {
                Weekly = result.Weekly,
                Monthly = result.Monthly,
                Yearly = result.Yearly,
                OneTimeThisYear = result.OneTimeThisYear,
                HomeCurrency = result.HomeCurrency,
                ByCategory = [.. result.ByCategory],
            });
        }
        catch (Exception)
        {
            var cached = (await _localCacheService.GetAllAsync<CachedBurnRate>()).FirstOrDefault();
            if (cached is not null)
            {
                ApplyBurnRate(cached.Weekly, cached.Monthly, cached.Yearly, cached.OneTimeThisYear, cached.HomeCurrency, cached.ByCategory);
                IsShowingCachedData = true;
            }
            else
            {
                ErrorMessage = "Couldn't load your dashboard. Please try again.";
                IsShowingCachedData = false;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyBurnRate(decimal weekly, decimal monthly, decimal yearly, decimal oneTimeThisYear, string homeCurrency, IReadOnlyList<CategoryBreakdownItem> byCategory)
    {
        Weekly = weekly;
        Monthly = monthly;
        Yearly = yearly;
        OneTimeThisYear = oneTimeThisYear;
        HomeCurrency = homeCurrency;

        ByCategory.Clear();
        foreach (var item in byCategory)
        {
            ByCategory.Add(item);
        }
    }
}
