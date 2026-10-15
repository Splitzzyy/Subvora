namespace SubVora.Application.PaymentSources;

public interface IPaymentSourceRepository
{
    Task<IReadOnlyList<PaymentSourceDto>> GetForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>A payment source this user owns, or null - including when the id belongs to someone else.</summary>
    Task<PaymentSourceDto?> GetByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Inserts with <see cref="CreatePaymentSourceRequest.Id"/> when the client chose one.</summary>
    Task<PaymentSourceDto> AddAsync(Guid userId, CreatePaymentSourceRequest request, CancellationToken cancellationToken = default);

    /// <summary>How many the user has, for the create quota in <see cref="UserQuotas"/>.</summary>
    Task<int> CountForUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user owns a row with this id - the cheap check for a replayed create.</summary>
    Task<bool> ExistsForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this user owns the payment source. Unlike categories there is no shared tier - every
    /// payment source belongs to exactly one user - so anything not owned is rejected outright.
    /// </summary>
    Task<bool> IsOwnedByUserAsync(Guid paymentSourceId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a payment source the user owns, in place. Delete-and-recreate is not equivalent:
    /// the foreign key is ON DELETE SET NULL, so every subscription pointing at the old row would
    /// quietly lose its payment source.
    /// </summary>
    Task<PaymentSourceDto?> UpdateAsync(Guid id, Guid userId, CreatePaymentSourceRequest request, CancellationToken cancellationToken = default);
}
