using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeSubscriptionsApi : ISubscriptionsApi
{
    public Func<Task<IReadOnlyList<SubscriptionDto>>> GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([]);

    public Task<IReadOnlyList<SubscriptionDto>> GetAllAsync(CancellationToken cancellationToken = default) => GetAllHandler();

    public Task<SubscriptionDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<SubscriptionDto> CreateAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<SubscriptionDto> UpdateAsync(Guid id, CreateSubscriptionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public Task<ResolveSubscriptionResponse> ResolveAsync(ResolveSubscriptionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
