using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeDashboardApi : IDashboardApi
{
    public Func<Task<BurnRateResult>> Handler = () => Task.FromResult(new BurnRateResult());

    public Task<BurnRateResult> GetBurnRateAsync(CancellationToken cancellationToken = default) => Handler();
}
