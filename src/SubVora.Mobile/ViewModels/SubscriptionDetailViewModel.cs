using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Billing;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class SubscriptionDetailViewModel : ObservableObject, IQueryAttributable
{
    private const int MinResolveInputLength = 3;

    private readonly ISubscriptionsApi _subscriptionsApi;
    private readonly ICategoriesApi _categoriesApi;
    private readonly IPaymentSourcesApi _paymentSourcesApi;
    private readonly IDebouncer _debouncer;
    private readonly IMessenger _messenger;
    private readonly IUserPrompt _userPrompt;
    private readonly IConnectivityService _connectivity;
    private ResolveSubscriptionResponse? _pendingSuggestion;

    // The catalog row this subscription is linked to, carried from a resolve result (or from the
    // record being edited) through to the save payload. Not an [ObservableProperty] - nothing in
    // the UI binds to it.
    private Guid? _appliedCatalogId;

    // The version of the record this edit started from, sent back on save so the server can tell
    // that the subscription has not moved on since. Null in add mode, where there is nothing to
    // conflict with.
    private uint? _loadedVersion;

    [ObservableProperty]
    public partial string CustomName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal CostAmount { get; set; }

    [ObservableProperty]
    public partial string Currency { get; set; } = "INR";

    [ObservableProperty]
    public partial BillingCycleType CycleCadence { get; set; } = BillingCycleType.Monthly;

    [ObservableProperty]
    public partial DateTime PurchaseDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial DateTime NextBillingDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    // Nullable and unset by default: leaving the field blank on the add screen sends no value,
    // which is what lets the server apply the user's global default from Settings.
    public partial int? AlertDaysAdvance { get; set; }

    [ObservableProperty]
    public partial bool IsFreeTrial { get; set; }

    [ObservableProperty]
    public partial CategoryDto? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial PaymentSourceDto? SelectedPaymentSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Whether the device has no network. Refreshed when the screen loads and after a failed write
    /// rather than by subscribing to the connectivity event: this view model is transient while
    /// IConnectivityService is a singleton, so a subscription would outlive the screen.
    /// <para>
    /// Only covers "this phone has no network". A reachable phone talking to a server that is down
    /// still reads as online - that case is caught by the write failing, with a message saying the
    /// change was not saved.
    /// </para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    public partial bool IsOffline { get; set; }

    /// <summary>Gates Save and Delete, so an edit that cannot possibly succeed is not offered.</summary>
    public bool CanSubmit => !IsOffline && !IsBusy;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial MatchConfidenceTier? SuggestedTier { get; set; }

    [ObservableProperty]
    public partial string? SuggestedProviderName { get; set; }

    [ObservableProperty]
    public partial string? SuggestedLogoUrl { get; set; }

    [ObservableProperty]
    public partial Guid? SubscriptionId { get; set; }

    [ObservableProperty]
    public partial bool IsEditMode { get; set; }

    [ObservableProperty]
    public partial string PageTitle { get; set; } = "Add Subscription";

    [ObservableProperty]
    public partial string SaveButtonText { get; set; } = "Save";

    public IReadOnlyList<BillingCycleType> BillingCycleTypes { get; } = Enum.GetValues<BillingCycleType>();

    public ObservableCollection<CategoryDto> Categories { get; } = [];

    public ObservableCollection<PaymentSourceDto> PaymentSources { get; } = [];

    /// <summary>Raised after a successful save so the view can navigate back.</summary>
    public event EventHandler? SaveSucceeded;

    /// <summary>Raised when loading an existing subscription 404s (deleted elsewhere) so the view can navigate back to the list.</summary>
    public event EventHandler? SubscriptionNotFound;

    /// <summary>Raised after a successful delete so the view can navigate back.</summary>
    public event EventHandler? Deleted;

    public SubscriptionDetailViewModel(ISubscriptionsApi subscriptionsApi, ICategoriesApi categoriesApi, IPaymentSourcesApi paymentSourcesApi, IDebouncer debouncer, IMessenger messenger, IUserPrompt userPrompt, IConnectivityService connectivity)
    {
        _subscriptionsApi = subscriptionsApi;
        _categoriesApi = categoriesApi;
        _paymentSourcesApi = paymentSourcesApi;
        _debouncer = debouncer;
        _messenger = messenger;
        _userPrompt = userPrompt;
        _connectivity = connectivity;

        IsOffline = !connectivity.IsConnected;

        // PurchaseDate and CycleCadence start at their defaults, so neither change handler has fired
        // yet and the add form would otherwise open showing today as the next billing date.
        RecalculateNextBillingDate();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && value is Guid id)
        {
            SubscriptionId = id;
        }
    }

    partial void OnSubscriptionIdChanged(Guid? value)
    {
        IsEditMode = value is not null;
    }

    partial void OnIsEditModeChanged(bool value)
    {
        PageTitle = value ? "Edit Subscription" : "Add Subscription";
        SaveButtonText = value ? "Save Changes" : "Save";
    }

    // Both feed the same derivation, so changing either re-derives the next billing date. A date the
    // user then picks by hand stands until they touch the purchase date or the cadence again, which
    // is also why LoadSubscriptionAsync assigns NextBillingDate last: a saved record's own date must
    // win over anything recomputed while its other fields were being applied.
    partial void OnPurchaseDateChanged(DateTime value) => RecalculateNextBillingDate();

    partial void OnCycleCadenceChanged(BillingCycleType value) => RecalculateNextBillingDate();

    private void RecalculateNextBillingDate() =>
        NextBillingDate = BillingCycleAdvancer.NextBillingDate(PurchaseDate, CycleCadence);

    partial void OnCustomNameChanged(string value)
    {
        SuggestedTier = null;
        _pendingSuggestion = null;

        // Editing the name away from an applied suggestion invalidates the match - a stale catalog
        // reference must never be saved. ApplySuggestion re-sets this *after* assigning CustomName,
        // which re-enters here.
        _appliedCatalogId = null;

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
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Client-side debouncing is a courtesy, not a guarantee against the server's own
            // rate limit - a 429 degrades silently, no error banner for that keystroke.
            return;
        }
        catch (Exception ex)
        {
            // Any other resolve failure (including being offline) is still worth surfacing,
            // since the user may not realize their typing isn't being resolved at all.
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
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

        // After the CustomName assignment above, whose change handler clears this.
        _appliedCatalogId = suggestion.CatalogId;
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
    private async Task InitializeAsync()
    {
        await LoadPickersAsync();
        await LoadSubscriptionAsync();
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
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
    }

    private async Task LoadSubscriptionAsync()
    {
        if (SubscriptionId is not Guid id)
        {
            return;
        }

        IsOffline = !_connectivity.IsConnected;

        try
        {
            var subscription = await _subscriptionsApi.GetByIdAsync(id);
            CustomName = subscription.CustomName;
            CostAmount = subscription.CostAmount;
            Currency = subscription.Currency;
            CycleCadence = subscription.CycleCadence;
            PurchaseDate = subscription.PurchaseDate.ToDateTime(TimeOnly.MinValue);
            NextBillingDate = subscription.NextBillingDate.ToDateTime(TimeOnly.MinValue);
            AlertDaysAdvance = subscription.AlertDaysAdvance;
            IsFreeTrial = subscription.IsFreeTrial;
            SelectedCategory = Categories.FirstOrDefault(c => c.Id == subscription.CategoryId);
            SelectedPaymentSource = PaymentSources.FirstOrDefault(p => p.Id == subscription.PaymentSourceId);
            // Last, because the CustomName assignment above clears it: saving an untouched edit
            // must not silently strip the record's existing catalog link.
            _appliedCatalogId = subscription.CatalogId;
            _loadedVersion = subscription.Version;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            SubscriptionNotFound?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
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
            if (IsEditMode && SubscriptionId is Guid id)
            {
                await _subscriptionsApi.UpdateAsync(id, request);
            }
            else
            {
                await _subscriptionsApi.CreateAsync(request);
            }

            // Only after the write succeeded - a failed save must not move the headline figure.
            _messenger.Send(new SubscriptionsChangedMessage());
            SaveSucceeded?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // The record moved on since this screen loaded it - most likely the charge was marked
            // paid on another device, which is exactly the case that used to be silently reversed
            // here. Reload so the form shows the current truth and carries a fresh version, then
            // say so: the edit is not applied, and re-applying it is the user's call, not ours.
            await LoadSubscriptionAsync();

            // ??= rather than =: SaveAsync cleared this on entry and LoadSubscriptionAsync only
            // writes to it on failure, so a non-null value here means the reload itself failed.
            // Claiming "we've reloaded it" in that case would be a lie on top of an error.
            ErrorMessage ??= "This subscription changed somewhere else. We've reloaded it - please check the values and save again.";
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deleting from the record you are already looking at. The list has swipe-to-delete, but a
    /// swipe advertises itself to nobody, so this is the discoverable path. Add mode has nothing to
    /// delete, hence the guard rather than only hiding the button.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SubscriptionId is not Guid id)
        {
            return;
        }

        var confirmed = await _userPrompt.ConfirmAsync(
            "Delete subscription",
            $"Delete {(string.IsNullOrWhiteSpace(CustomName) ? "this subscription" : CustomName)}? This cannot be undone.",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _subscriptionsApi.DeleteAsync(id);

            // Only after the server accepted it - a failed delete must not move the headline figure
            // or bounce the user off a record that still exists.
            _messenger.Send(new SubscriptionsChangedMessage());
            Deleted?.Invoke(this, EventArgs.Empty);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone - the outcome the user asked for, so treat it as success rather than
            // stranding them on a record that no longer exists.
            _messenger.Send(new SubscriptionsChangedMessage());
            Deleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private CreateSubscriptionRequest BuildRequest() => new()
    {
        Version = _loadedVersion,
        CustomName = CustomName,
        CostAmount = CostAmount,
        Currency = Currency,
        CycleCadence = CycleCadence,
        PurchaseDate = DateOnly.FromDateTime(PurchaseDate),
        NextBillingDate = DateOnly.FromDateTime(NextBillingDate),
        AlertDaysAdvance = AlertDaysAdvance,
        CategoryId = SelectedCategory?.Id,
        PaymentSourceId = SelectedPaymentSource?.Id,
        CatalogId = _appliedCatalogId,
        IsFreeTrial = IsFreeTrial,
    };
}
