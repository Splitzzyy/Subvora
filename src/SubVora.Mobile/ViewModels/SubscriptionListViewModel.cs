using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Notifications;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class SubscriptionListViewModel : ObservableObject
{
    private readonly ISubscriptionsApi _subscriptionsApi;
    private readonly ILocalCacheService _localCacheService;
    private readonly IUserPrompt _userPrompt;
    private readonly IMessenger _messenger;
    private readonly IRenewalNotificationScheduler _notificationScheduler;
    private readonly IConnectivityService _connectivity;

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsShowingCachedData { get; set; }

    /// <summary>
    /// Whether a write is in flight. Separate from <see cref="IsLoading"/>, which drives the pull-to-
    /// refresh spinner: a second mark-paid landing on top of the first would advance the billing date
    /// two cycles, so the write path needs its own gate.
    /// </summary>
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

    /// <summary>
    /// Gates swipe-to-delete and mark-as-paid, so a write that cannot possibly succeed is not
    /// offered. This list is the screen most likely to be looked at offline - it serves itself from
    /// the SQLite mirror - which is exactly why the two writes on it have to be closed rather than
    /// left to fail.
    /// </summary>
    public bool CanSubmit => !IsOffline && !IsBusy;

    /// <summary>
    /// Flat, in server order. Stays the authoritative set - the notification scheduler, the cache
    /// write-back and delete all read it - while <see cref="Groups"/> is the display projection.
    /// </summary>
    public ObservableCollection<SubscriptionDto> Subscriptions { get; } = [];

    /// <summary>
    /// What the list actually renders: one group per category, groups ordered by whichever bills
    /// soonest and rows within a group likewise, so the next thing to be charged is at the top of
    /// the screen rather than wherever the server happened to return it.
    /// </summary>
    public ObservableCollection<SubscriptionGroup> Groups { get; } = [];

    /// <summary>Raised when a row is tapped, to navigate to the detail screen in edit mode.</summary>
    public event EventHandler<Guid>? SubscriptionSelected;

    /// <summary>Raised by the Add toolbar button, to navigate to the detail screen in add mode.</summary>
    public event EventHandler? AddRequested;

    public SubscriptionListViewModel(
        ISubscriptionsApi subscriptionsApi,
        ILocalCacheService localCacheService,
        IUserPrompt userPrompt,
        IMessenger messenger,
        IRenewalNotificationScheduler notificationScheduler,
        IConnectivityService connectivity)
    {
        _subscriptionsApi = subscriptionsApi;
        _localCacheService = localCacheService;
        _userPrompt = userPrompt;
        _messenger = messenger;
        _notificationScheduler = notificationScheduler;
        _connectivity = connectivity;

        IsOffline = !connectivity.IsConnected;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        IsOffline = !_connectivity.IsConnected;
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
        // Filtered, not a bare catch: this branch falls back to the SQLite mirror, so swallowing a
        // defect here would show stale rows under "showing last synced data" while the phone is on
        // wifi - the one failure nobody ever reports, because it looks like it is working.
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
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
                ErrorMessage = ApiErrorMapper.ToDisplayMessage(ex);
                IsShowingCachedData = false;
            }
        }
        finally
        {
            // Whichever branch above won - live, cached, or neither - Groups has to match the flat
            // list, including being emptied when a failure left nothing to show.
            RebuildGroups();
            IsLoading = false;
        }

        // Reschedule from whichever list won above - live or cached. This is the only place that
        // holds the authoritative set, and it runs on every appearance of the list, so an add or
        // edit is picked up when navigation returns here.
        // ponytail: scheduling refreshes only when this screen loads. If reminders need to follow
        // an edit made without visiting the list, move this behind a SubscriptionsChangedMessage
        // subscriber that reads the local cache.
        await _notificationScheduler.SyncAsync(Subscriptions);
    }

    /// <summary>
    /// Rebuilds <see cref="Groups"/> from <see cref="Subscriptions"/>. Called from every place that
    /// mutates the flat list, so the two cannot drift apart.
    /// </summary>
    private void RebuildGroups()
    {
        Groups.Clear();

        var grouped = Subscriptions
            .GroupBy(s => string.IsNullOrWhiteSpace(s.CategoryName) ? SubscriptionGroup.UncategorisedName : s.CategoryName!)
            .Select(g => new SubscriptionGroup(g.Key, g.OrderBy(s => s.NextBillingDate).ThenBy(s => s.CustomName)))
            .OrderBy(g => g.NextBillingDate)
            // Two categories billing the same day would otherwise reorder between loads.
            .ThenBy(g => g.CategoryName, StringComparer.CurrentCultureIgnoreCase);

        foreach (var group in grouped)
        {
            Groups.Add(group);
        }
    }

    /// <summary>
    /// Settles the outstanding charge. The server decides the new billing date - it moves one cycle
    /// from the date just paid, not from today - so the row is replaced with what came back rather
    /// than with anything guessed here.
    /// </summary>
    [RelayCommand]
    private async Task MarkPaidAsync(Guid id)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var updated = await _subscriptionsApi.MarkPaidAsync(id);

            var index = Subscriptions.ToList().FindIndex(s => s.Id == id);
            if (index >= 0)
            {
                Subscriptions[index] = updated;
                RebuildGroups();
            }

            await _localCacheService.UpsertAsync(CachedSubscription.FromDto(updated));

            // The paid charge changes nothing about the burn rate, but a OneTime subscription
            // deactivates on payment, and that does move the dashboard.
            _messenger.Send(new SubscriptionsChangedMessage());
            await _notificationScheduler.SyncAsync(Subscriptions);
        }
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            // Write wording, not read wording. Nothing is queued for later - the mirror is refreshed
            // from successful GETs only - so a charge that failed to settle has to say so, or the
            // row keeps its OVERDUE chip while the user believes they have cleared it.
            IsOffline = !_connectivity.IsConnected;
            ErrorMessage = ApiErrorMapper.ToWriteFailureMessage(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SelectSubscription(Guid id) => SubscriptionSelected?.Invoke(this, id);

    [RelayCommand]
    private void Add() => AddRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task DeleteSubscriptionAsync(Guid id)
    {
        var confirmed = await _userPrompt.ConfirmAsync(
            "Delete subscription",
            "Are you sure you want to delete this subscription?",
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

            var toRemove = Subscriptions.FirstOrDefault(s => s.Id == id);
            if (toRemove is not null)
            {
                Subscriptions.Remove(toRemove);
                RebuildGroups();
            }

            await _localCacheService.ClearAsync<CachedSubscription>();
            foreach (var subscription in Subscriptions)
            {
                await _localCacheService.UpsertAsync(CachedSubscription.FromDto(subscription));
            }

            _messenger.Send(new SubscriptionsChangedMessage());
            await _notificationScheduler.SyncAsync(Subscriptions);
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
}
