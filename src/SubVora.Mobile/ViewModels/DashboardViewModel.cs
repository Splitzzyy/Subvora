using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IDashboardApi _dashboardApi;

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

    public ObservableCollection<CategoryBreakdownItem> ByCategory { get; } = [];

    public DashboardViewModel(IDashboardApi dashboardApi)
    {
        _dashboardApi = dashboardApi;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _dashboardApi.GetBurnRateAsync();

            Weekly = result.Weekly;
            Monthly = result.Monthly;
            Yearly = result.Yearly;
            OneTimeThisYear = result.OneTimeThisYear;
            HomeCurrency = result.HomeCurrency;

            ByCategory.Clear();
            foreach (var item in result.ByCategory)
            {
                ByCategory.Add(item);
            }
        }
        catch (Exception)
        {
            ErrorMessage = "Couldn't load your dashboard. Please try again.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
