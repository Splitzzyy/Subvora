namespace SubVora.Application.PaymentSources;

public interface IPaymentSourceRepository
{
    Task<IReadOnlyList<PaymentSourceDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PaymentSourceDto> AddAsync(Guid userId, CreatePaymentSourceRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
}
