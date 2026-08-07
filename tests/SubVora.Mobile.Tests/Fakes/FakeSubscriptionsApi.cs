using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakeSubscriptionsApi : ISubscriptionsApi
{
    public Func<Task<IReadOnlyList<SubscriptionDto>>> GetAllHandler = () => Task.FromResult<IReadOnlyList<SubscriptionDto>>([]);
    public Func<Guid, Task<SubscriptionDto>> GetByIdHandler = _ => throw new NotImplementedException();
    public Func<CreateSubscriptionRequest, Task<SubscriptionDto>> CreateHandler =
        request => Task.FromResult(new SubscriptionDto
        {
            Id = Guid.NewGuid(),
            CustomName = request.CustomName,
            CostAmount = request.CostAmount,
            Currency = request.Currency,
            CycleCadence = request.CycleCadence,
            PurchaseDate = request.PurchaseDate,
            NextBillingDate = request.NextBillingDate,
            // Stands in for the server resolving an omitted lead time from the user's global default.
            AlertDaysAdvance = request.AlertDaysAdvance ?? 3,
            CategoryId = request.CategoryId,
            PaymentSourceId = request.PaymentSourceId,
            IsFreeTrial = request.IsFreeTrial,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    public Func<Guid, CreateSubscriptionRequest, Task<SubscriptionDto>> UpdateHandler = (_, _) => throw new NotImplementedException();
    public Func<Guid, Task> DeleteHandler = _ => Task.CompletedTask;
    public Func<Guid, Task<SubscriptionDto>> MarkPaidHandler = _ => throw new NotImplementedException();
    public Func<ResolveSubscriptionRequest, Task<ResolveSubscriptionResponse>> ResolveHandler =
        _ => Task.FromResult(new ResolveSubscriptionResponse { Tier = MatchConfidenceTier.Manual });

    public List<CreateSubscriptionRequest> CreateCalls { get; } = [];
    public List<Guid> DeleteCalls { get; } = [];
    public List<Guid> MarkPaidCalls { get; } = [];
    public List<ResolveSubscriptionRequest> ResolveCalls { get; } = [];

    public Task<IReadOnlyList<SubscriptionDto>> GetAllAsync(CancellationToken cancellationToken = default) => GetAllHandler();

    public Task<SubscriptionDto> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => GetByIdHandler(id);

    public Task<SubscriptionDto> CreateAsync(CreateSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        CreateCalls.Add(request);
        return CreateHandler(request);
    }

    public Task<SubscriptionDto> UpdateAsync(Guid id, CreateSubscriptionRequest request, CancellationToken cancellationToken = default) => UpdateHandler(id, request);

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add(id);
        return DeleteHandler(id);
    }

    public Task<SubscriptionDto> MarkPaidAsync(Guid id, CancellationToken cancellationToken = default)
    {
        MarkPaidCalls.Add(id);
        return MarkPaidHandler(id);
    }

    public Task<ResolveSubscriptionResponse> ResolveAsync(ResolveSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        ResolveCalls.Add(request);
        return ResolveHandler(request);
    }
}
