using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface ISubscriptionsApi
{
    [Get("/api/v1/subscriptions")]
    Task<IReadOnlyList<SubscriptionDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [Get("/api/v1/subscriptions/{id}")]
    Task<SubscriptionDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    [Post("/api/v1/subscriptions")]
    Task<SubscriptionDto> CreateAsync([Body] CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    [Put("/api/v1/subscriptions/{id}")]
    Task<SubscriptionDto> UpdateAsync(Guid id, [Body] CreateSubscriptionRequest request, CancellationToken cancellationToken = default);

    [Delete("/api/v1/subscriptions/{id}")]
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    [Post("/api/v1/subscriptions/resolve")]
    Task<ResolveSubscriptionResponse> ResolveAsync([Body] ResolveSubscriptionRequest request, CancellationToken cancellationToken = default);
}
