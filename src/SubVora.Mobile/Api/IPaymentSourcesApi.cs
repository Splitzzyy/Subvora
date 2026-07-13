using Refit;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Api;

public interface IPaymentSourcesApi
{
    [Get("/api/v1/payment-sources")]
    Task<IReadOnlyList<PaymentSourceDto>> GetAllAsync(CancellationToken cancellationToken = default);

    [Post("/api/v1/payment-sources")]
    Task<PaymentSourceDto> CreateAsync([Body] CreatePaymentSourceRequest request, CancellationToken cancellationToken = default);

    [Delete("/api/v1/payment-sources/{id}")]
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
