using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SubVora.Mobile.Api;
using SubVora.Mobile.Messages;
using SubVora.Mobile.Api.Dtos;
using SubVora.Mobile.Models;
using SubVora.Mobile.Services;

namespace SubVora.Mobile.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private const int StaleRateAgeDays = 2;

    private readonly IDashboardApi _dashboardApi;
    private readonly ILocalCacheService _localCacheService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial decimal Weekly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial decimal Monthly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial decimal Yearly { get; set; }

    [ObservableProperty]
    public partial decimal OneTimeThisYear { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string HomeCurrency { get; set; } = string.Empty;

    /// <summary>
    /// One-line form of the same figures for the app-wide banner, e.g.
    /// "USD 3.73/wk | 15.99/mo | 194.54/yr". Empty until a load succeeds, which is also what hides
    /// the banner: there is no separate visibility flag to keep in sync, and a signed-out user has
    /// nothing to show.
    /// </summary>
    public string Summary => string.IsNullOrEmpty(HomeCurrency)
        ? string.Empty
        : $"{HomeCurrency} {Weekly:N2}/wk | {Monthly:N2}/mo | {Yearly:N2}/yr";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsShowingCachedData { get; set; }

    /// <summary>
    /// Why the headline number may not be the whole story - subscriptions the server could not
    /// convert, or rates old enough to be worth saying out loud. Null when the totals are clean.
    /// </summary>
    [ObservableProperty]
    public partial string? WarningMessage { get; set; }

    public ObservableCollection<CategoryBreakdownItem> ByCategory { get; } = [];

    public DashboardViewModel(IDashboardApi dashboardApi, ILocalCacheService localCacheService, IMessenger messenger)
    {
        _dashboardApi = dashboardApi;
        _localCacheService = localCacheService;

        // Registered for the lifetime of this view model. It is a singleton (see MauiProgram) so
        // the banner and the dashboard page always read the same numbers from one fetch.
        messenger.Register<SubscriptionsChangedMessage>(this, (_, _) => LoadCommand.Execute(null));
        messenger.Register<SessionEndedMessage>(this, (_, _) => Clear());
    }

    /// <summary>
    /// Drops the figures so the banner cannot outlive the session that produced them - a signed-out
    /// or expired user must not still see their spend on the login screen.
    /// </summary>
    public void Clear()
    {
        Weekly = 0;
        Monthly = 0;
        Yearly = 0;
        OneTimeThisYear = 0;
        HomeCurrency = string.Empty;
        WarningMessage = null;
        ErrorMessage = null;
        IsShowingCachedData = false;
        ByCategory.Clear();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var result = await _dashboardApi.GetBurnRateAsync();

            // The cache model doubles as the view's input shape, so the live and offline paths
            // apply exactly the same fields - a new one can't be wired up on only one of them.
            var snapshot = new CachedBurnRate
            {
                Weekly = result.Weekly,
                Monthly = result.Monthly,
                Yearly = result.Yearly,
                OneTimeThisYear = result.OneTimeThisYear,
                HomeCurrency = result.HomeCurrency,
                UnresolvedSubscriptionCount = result.UnresolvedSubscriptionIds.Count,
                OldestRateFetchedAt = result.OldestRateFetchedAt,
                ByCategory = [.. result.ByCategory],
            };

            ApplyBurnRate(snapshot);
            IsShowingCachedData = false;

            await _localCacheService.UpsertAsync(snapshot);
        }
        // Filtered, not a bare catch: the branch below falls back to the cached snapshot, so a
        // defect swallowed here would quietly show yesterday's totals as though the network were
        // the problem. ApiErrorMapper.IsApiFailure is what separates the two.
        catch (Exception ex) when (ApiErrorMapper.IsApiFailure(ex))
        {
            var cached = (await _localCacheService.GetAllAsync<CachedBurnRate>()).FirstOrDefault();
            if (cached is not null)
            {
                ApplyBurnRate(cached);
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
            IsLoading = false;
        }
    }

    private void ApplyBurnRate(CachedBurnRate snapshot)
    {
        Weekly = snapshot.Weekly;
        Monthly = snapshot.Monthly;
        Yearly = snapshot.Yearly;
        OneTimeThisYear = snapshot.OneTimeThisYear;
        HomeCurrency = snapshot.HomeCurrency;
        WarningMessage = BuildWarningMessage(snapshot.UnresolvedSubscriptionCount, snapshot.OldestRateFetchedAt);

        ByCategory.Clear();

        // Bars are normalised against the largest category, not the total: split across eight
        // categories a share-of-total bar is a stub for everything below the leader, and the
        // comparison the list exists to support stops being readable.
        var largest = snapshot.ByCategory.Count == 0 ? 0m : snapshot.ByCategory.Max(i => i.MonthlyAmount);
        foreach (var item in snapshot.ByCategory)
        {
            item.Share = largest > 0 ? (double)(item.MonthlyAmount / largest) : 0;
            ByCategory.Add(item);
        }
    }

    private static string? BuildWarningMessage(int unresolvedCount, DateTimeOffset? oldestRateFetchedAt)
    {
        var warnings = new List<string>();

        if (unresolvedCount > 0)
        {
            var subject = unresolvedCount == 1 ? "1 subscription is" : $"{unresolvedCount} subscriptions are";
            warnings.Add($"{subject} not included — no exchange rate available yet.");
        }

        // The server refreshes rates every 24h, so anything this old means at least one run was
        // missed. The rate is still used; the user just gets told how old it is.
        if (oldestRateFetchedAt is { } fetchedAt)
        {
            var ageDays = (int)(DateTimeOffset.UtcNow - fetchedAt).TotalDays;
            if (ageDays >= StaleRateAgeDays)
            {
                warnings.Add($"Converted using exchange rates from {ageDays} days ago.");
            }
        }

        return warnings.Count == 0 ? null : string.Join(" ", warnings);
    }
}
