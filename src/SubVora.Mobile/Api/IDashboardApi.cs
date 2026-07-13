using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface IDashboardApi
{
    [Get("/api/v1/dashboard/burn-rate")]
    Task<BurnRateResult> GetBurnRateAsync(CancellationToken cancellationToken = default);
}
