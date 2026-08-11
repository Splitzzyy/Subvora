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

            // Where the next load will also put it. Appending sent the new row to the bottom, and
            // the server's OrderBy(p => p.Label) then relocated it on the next visit.
            PaymentSources.Insert(SortedIndexFor(created.Label), created);

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

    /// <summary>
    /// Renames a payment source in place. Deliberately not delete-and-recreate: the subscriptions
    /// foreign key is ON DELETE SET NULL, so recreating would silently detach every subscription
    /// that billed to it.
    /// </summary>
    [RelayCommand]
    private async Task RenameAsync(PaymentSourceDto paymentSource)
    {
        var newLabel = await _userPrompt.PromptAsync("Rename payment source", "New label", paymentSource.Label);
        if (string.IsNullOrWhiteSpace(newLabel) || newLabel.Trim() == paymentSource.Label)
        {
            return;
        }

        ErrorMessage = null;
        try
        {
            var updated = await _paymentSourcesApi.UpdateAsync(paymentSource.Id, new CreatePaymentSourceRequest
            {
                Label = newLabel.Trim(),
                // Unchanged - this action renames, and the type is set when the source is created.
                SourceType = paymentSource.SourceType,
            });

            // Replaced rather than mutated: PaymentSourceDto is not observable, so the row would
            // not repaint. Removed and re-inserted rather than swapped in place, because a rename
            // that changes the first letter changes where the row belongs - leaving it put would
            // move it on the next load instead.
            var index = PaymentSources.IndexOf(paymentSource);
            if (index >= 0)
            {
                PaymentSources.RemoveAt(index);
                PaymentSources.Insert(SortedIndexFor(updated.Label), updated);
            }
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
    }

    /// <summary>
    /// Where a label belongs in the list, matching the server's <c>OrderBy(p =&gt; p.Label)</c>.
    /// <para>
    /// CurrentCultureIgnoreCase against Postgres' own collation: the two agree for ASCII labels and
    /// can differ on accents, in which case the next load re-sorts. That beats every new row landing
    /// at the bottom and moving later.
    /// </para>
    /// </summary>
    private int SortedIndexFor(string label) =>
        PaymentSources.TakeWhile(existing =>
            string.Compare(existing.Label, label, StringComparison.CurrentCultureIgnoreCase) < 0).Count();

    /// <summary>
    /// The discoverable route to rename/delete, behind the row's manage button. Swipe still works
    /// and is faster, but nothing on screen ever said it was there.
    /// </summary>
    [RelayCommand]
    private async Task ManageAsync(PaymentSourceDto paymentSource)
    {
        var choice = await _userPrompt.ActionSheetAsync(paymentSource.Label, "Cancel", "Rename", "Delete");

        switch (choice)
        {
            case "Rename":
                await RenameAsync(paymentSource);
                break;
            case "Delete":
                await DeleteAsync(paymentSource.Id);
                break;
            // Dismissed, or something we did not offer - do nothing.
        }
    }
}
