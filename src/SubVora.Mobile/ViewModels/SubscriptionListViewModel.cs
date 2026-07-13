using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class SubscriptionListViewModel : ObservableObject
{
    private readonly ISubscriptionsApi _subscriptionsApi;
    private readonly ILocalCacheService _localCacheService;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsShowingCachedData { get; set; }

    public ObservableCollection<SubscriptionDto> Subscriptions { get; } = [];

    /// <summary>Raised when a row is tapped. The detail screen doesn't exist yet (a later slice).</summary>
    public event EventHandler<Guid>? SubscriptionSelected;

    /// <summary>Raised by the Add toolbar button. The add screen doesn't exist yet (a later slice).</summary>
    public event EventHandler? AddRequested;

    public SubscriptionListViewModel(ISubscriptionsApi subscriptionsApi, ILocalCacheService localCacheService)
    {
        _subscriptionsApi = subscriptionsApi;
        _localCacheService = localCacheService;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _subscriptionsApi.GetAllAsync();

            Subscriptions.Clear();
            foreach (var subscription in result)
            {
                Subscriptions.Add(subscription);
            }

            IsShowingCachedData = false;

            await _localCacheService.ClearAsync<CachedSubscription>();
            foreach (var subscription in result)
            {
                await _localCacheService.UpsertAsync(CachedSubscription.FromDto(subscription));
            }
        }
        catch (Exception)
        {
            var cached = await _localCacheService.GetAllAsync<CachedSubscription>();
            if (cached.Count > 0)
            {
                Subscriptions.Clear();
                foreach (var item in cached)
                {
                    Subscriptions.Add(item.ToDto());
                }

                IsShowingCachedData = true;
            }
            else
            {
                ErrorMessage = "Couldn't load your subscriptions. Please try again.";
                IsShowingCachedData = false;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectSubscription(Guid id) => SubscriptionSelected?.Invoke(this, id);

    [RelayCommand]
    private void Add() => AddRequested?.Invoke(this, EventArgs.Empty);
}
