using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class PaymentSourcesViewModel : ObservableObject
{
    private readonly IPaymentSourcesApi _paymentSourcesApi;
    private readonly IUserPrompt _userPrompt;
    private readonly IConnectivityService _connectivity;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string NewLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PaymentSourceType NewSourceType { get; set; } = PaymentSourceType.Other;

    public IReadOnlyList<PaymentSourceType> SourceTypes { get; } = Enum.GetValues<PaymentSourceType>();

    public ObservableCollection<PaymentSourceDto> PaymentSources { get; } = [];

    public PaymentSourcesViewModel(IPaymentSourcesApi paymentSourcesApi, IUserPrompt userPrompt, IConnectivityService connectivity)
    {
        _connectivity = connectivity;
        IsOffline = !connectivity.IsConnected;
        _paymentSourcesApi = paymentSourcesApi;
        _userPrompt = userPrompt;
    }
    /// <summary>
    /// Whether the device has no network. Refreshed when the screen loads and after a failed write
    /// rather than by subscribing to the connectivity event: these view models are transient while
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
            var paymentSources = await _paymentSourcesApi.GetAllAsync();
            PaymentSources.Clear();
            foreach (var paymentSource in paymentSources)
            {
                PaymentSources.Add(paymentSource);
            }
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            // A read, so the plain wording: nothing was lost, there is just nothing to show.
            IsOffline = !_connectivity.IsConnected;
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
            var created = await _paymentSourcesApi.CreateAsync(new CreatePaymentSourceRequest { Label = NewLabel, SourceType = NewSourceType });
            PaymentSources.Add(created);
            NewLabel = string.Empty;
            NewSourceType = PaymentSourceType.Other;
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(Guid id)
    {
        var confirmed = await _userPrompt.ConfirmAsync(
            "Delete payment source",
            "Are you sure you want to delete this payment source?",
            "Delete",
            "Cancel");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _paymentSourcesApi.DeleteAsync(id);
            var toRemove = PaymentSources.FirstOrDefault(p => p.Id == id);
            if (toRemove is not null)
            {
                PaymentSources.Remove(toRemove);
            }
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }
}
