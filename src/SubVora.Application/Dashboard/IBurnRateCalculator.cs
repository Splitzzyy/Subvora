using SubVora.Application.Subscriptions;

namespace SubVora.Application.Dashboard;

public interface IBurnRateCalculator
{
    Task<BurnRateResult> CalculateAsync(IEnumerable<SubscriptionDto> subscriptions, string homeCurrency, CancellationToken cancellationToken = default);
}
