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
    private const int MinResolveInputLength = 3;

    private readonly ISubscriptionsApi _subscriptionsApi;
    private readonly ICategoriesApi _categoriesApi;
    private readonly IPaymentSourcesApi _paymentSourcesApi;
    private readonly IDebouncer _debouncer;
    private ResolveSubscriptionResponse? _pendingSuggestion;

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

    [ObservableProperty]
    public partial MatchConfidenceTier? SuggestedTier { get; set; }

    [ObservableProperty]
    public partial string? SuggestedProviderName { get; set; }

    [ObservableProperty]
    public partial string? SuggestedLogoUrl { get; set; }

    public IReadOnlyList<BillingCycleType> BillingCycleTypes { get; } = Enum.GetValues<BillingCycleType>();

    public ObservableCollection<CategoryDto> Categories { get; } = [];

    public ObservableCollection<PaymentSourceDto> PaymentSources { get; } = [];

    /// <summary>Raised after a successful save so the view can navigate back.</summary>
    public event EventHandler? SaveSucceeded;

    public SubscriptionDetailViewModel(ISubscriptionsApi subscriptionsApi, ICategoriesApi categoriesApi, IPaymentSourcesApi paymentSourcesApi, IDebouncer debouncer)
    {
        _subscriptionsApi = subscriptionsApi;
        _categoriesApi = categoriesApi;
        _paymentSourcesApi = paymentSourcesApi;
        _debouncer = debouncer;
    }

    partial void OnCustomNameChanged(string value)
    {
        SuggestedTier = null;
        _pendingSuggestion = null;

        if (value.Length < MinResolveInputLength)
        {
            return;
        }

        _debouncer.Debounce(() => _ = ResolveNameAsync(value));
    }

    private async Task ResolveNameAsync(string input)
    {
        ResolveSubscriptionResponse result;
        try
        {
            result = await _subscriptionsApi.ResolveAsync(new ResolveSubscriptionRequest { Input = input });
        }
        catch (ApiException)
        {
            // Includes 429 (client-side debouncing is a courtesy, not a guarantee against the
            // server's own rate limit) - a resolve failure degrades silently, no error banner.
            return;
        }

        switch (result.Tier)
        {
            case MatchConfidenceTier.AutoFill:
                ApplySuggestion(result);
                break;
            case MatchConfidenceTier.SuggestConfirm:
                _pendingSuggestion = result;
                SuggestedTier = MatchConfidenceTier.SuggestConfirm;
                SuggestedProviderName = result.ProviderName;
                SuggestedLogoUrl = result.LogoUrl;
                break;
            case MatchConfidenceTier.Manual:
            default:
                break;
        }
    }

    [RelayCommand]
    private void AcceptSuggestion()
    {
        if (_pendingSuggestion is null)
        {
            return;
        }

        ApplySuggestion(_pendingSuggestion);
    }

    private void ApplySuggestion(ResolveSubscriptionResponse suggestion)
    {
        if (!string.IsNullOrEmpty(suggestion.ProviderName))
        {
            CustomName = suggestion.ProviderName;
        }

        SuggestedLogoUrl = suggestion.LogoUrl;

        if (suggestion.CategoryId is Guid categoryId)
        {
            var match = Categories.FirstOrDefault(c => c.Id == categoryId);
            if (match is not null)
            {
                SelectedCategory = match;
            }
        }

        SuggestedTier = null;
        _pendingSuggestion = null;
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
