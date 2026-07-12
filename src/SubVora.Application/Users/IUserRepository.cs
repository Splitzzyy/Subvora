namespace SubVora.Application.Users;

public interface IUserRepository
{
    Task<string?> GetPreferredCurrencyAsync(Guid userId, CancellationToken cancellationToken = default);
}
