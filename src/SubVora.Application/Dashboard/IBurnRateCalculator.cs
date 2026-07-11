using SubVora.Application.Subscriptions;

namespace SubVora.Application.Dashboard;

public interface IBurnRateCalculator
{
    BurnRateResult Calculate(IEnumerable<SubscriptionDto> subscriptions);
}
