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

    public PaymentSourcesViewModel(IPaymentSourcesApi paymentSourcesApi, IUserPrompt userPrompt)
    {
        _paymentSourcesApi = paymentSourcesApi;
        _userPrompt = userPrompt;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var paymentSources = await _paymentSourcesApi.GetAllAsync();
            PaymentSources.Clear();
            foreach (var paymentSource in paymentSources)
            {
                PaymentSources.Add(paymentSource);
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
            var created = await _paymentSourcesApi.CreateAsync(new CreatePaymentSourceRequest { Label = NewLabel, SourceType = NewSourceType });
            PaymentSources.Add(created);
            NewLabel = string.Empty;
            NewSourceType = PaymentSourceType.Other;
        }
        catch (ApiException ex)
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
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
        catch (ApiException ex)
        {
            ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
        }
    }
}
