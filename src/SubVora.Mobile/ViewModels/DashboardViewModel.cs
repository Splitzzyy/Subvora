using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SubVora.Mobile.Api;
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

    [ObservableProperty]
    public partial bool IsShowingCachedData { get; set; }

    /// <summary>
    /// Why the headline number may not be the whole story - subscriptions the server could not
    /// convert, or rates old enough to be worth saying out loud. Null when the totals are clean.
    /// </summary>
    [ObservableProperty]
    public partial string? WarningMessage { get; set; }

    public ObservableCollection<CategoryBreakdownItem> ByCategory { get; } = [];

    public DashboardViewModel(IDashboardApi dashboardApi, ILocalCacheService localCacheService)
    {
        _dashboardApi = dashboardApi;
        _localCacheService = localCacheService;
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
        catch (Exception ex)
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
        foreach (var item in snapshot.ByCategory)
        {
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
