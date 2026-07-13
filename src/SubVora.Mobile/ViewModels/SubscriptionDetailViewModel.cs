using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class SubscriptionDetailViewModel : ObservableObject
{
    private readonly ISubscriptionsApi _subscriptionsApi;
    private readonly ICategoriesApi _categoriesApi;
    private readonly IPaymentSourcesApi _paymentSourcesApi;

    [ObservableProperty]
    public partial string CustomName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal CostAmount { get; set; }

    [ObservableProperty]
    public partial string Currency { get; set; } = "USD";

    [ObservableProperty]
    public partial BillingCycleType CycleCadence { get; set; } = BillingCycleType.Monthly;

    [ObservableProperty]
    public partial DateTime PurchaseDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial DateTime NextBillingDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial int AlertDaysAdvance { get; set; } = 3;

    [ObservableProperty]
    public partial bool IsFreeTrial { get; set; }

    [ObservableProperty]
    public partial CategoryDto? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial PaymentSourceDto? SelectedPaymentSource { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public IReadOnlyList<BillingCycleType> BillingCycleTypes { get; } = Enum.GetValues<BillingCycleType>();

    public ObservableCollection<CategoryDto> Categories { get; } = [];

    public ObservableCollection<PaymentSourceDto> PaymentSources { get; } = [];

    /// <summary>Raised after a successful save so the view can navigate back.</summary>
    public event EventHandler? SaveSucceeded;

    public SubscriptionDetailViewModel(ISubscriptionsApi subscriptionsApi, ICategoriesApi categoriesApi, IPaymentSourcesApi paymentSourcesApi)
    {
        _subscriptionsApi = subscriptionsApi;
        _categoriesApi = categoriesApi;
        _paymentSourcesApi = paymentSourcesApi;
    }

    [RelayCommand]
    private async Task LoadPickersAsync()
    {
        try
        {
            var categories = await _categoriesApi.GetAllAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }

            var paymentSources = await _paymentSourcesApi.GetAllAsync();
            PaymentSources.Clear();
            foreach (var paymentSource in paymentSources)
            {
                PaymentSources.Add(paymentSource);
            }
        }
        catch (ApiException)
        {
            ErrorMessage = "Couldn't load categories/payment sources. Please try again.";
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var request = BuildRequest();
            await _subscriptionsApi.CreateAsync(request);
            SaveSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex)
        {
            ErrorMessage = ApiValidationErrorParser.ExtractFirstMessage(ex) ?? "Couldn't save this subscription. Please check the form and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CreateSubscriptionRequest BuildRequest() => new()
    {
        CustomName = CustomName,
        CostAmount = CostAmount,
        Currency = Currency,
        CycleCadence = CycleCadence,
        PurchaseDate = DateOnly.FromDateTime(PurchaseDate),
        NextBillingDate = DateOnly.FromDateTime(NextBillingDate),
        AlertDaysAdvance = AlertDaysAdvance,
        CategoryId = SelectedCategory?.Id,
        PaymentSourceId = SelectedPaymentSource?.Id,
        IsFreeTrial = IsFreeTrial,
    };
}
