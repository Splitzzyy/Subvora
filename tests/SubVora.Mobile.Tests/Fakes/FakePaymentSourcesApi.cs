using SubVora.Mobile.Api;
using SubVora.Mobile.Api.Dtos;

namespace SubVora.Mobile.Tests.Fakes;

public class FakePaymentSourcesApi : IPaymentSourcesApi
{
    public Func<Task<IReadOnlyList<PaymentSourceDto>>> GetAllHandler = () => Task.FromResult<IReadOnlyList<PaymentSourceDto>>([]);
    public Func<CreatePaymentSourceRequest, Task<PaymentSourceDto>> CreateHandler =
        request => Task.FromResult(new PaymentSourceDto { Id = Guid.NewGuid(), Label = request.Label, SourceType = request.SourceType });
    public Func<Guid, Task> DeleteHandler = _ => Task.CompletedTask;

    public Task<IReadOnlyList<PaymentSourceDto>> GetAllAsync(CancellationToken cancellationToken = default) => GetAllHandler();

    public Task<PaymentSourceDto> CreateAsync(CreatePaymentSourceRequest request, CancellationToken cancellationToken = default) => CreateHandler(request);

    public Func<Guid, CreatePaymentSourceRequest, Task<PaymentSourceDto>> UpdateHandler =
        (id, request) => Task.FromResult(new PaymentSourceDto { Id = id, Label = request.Label, SourceType = request.SourceType });

    public List<(Guid Id, CreatePaymentSourceRequest Request)> UpdateCalls { get; } = [];

    public Task<PaymentSourceDto> UpdateAsync(Guid id, CreatePaymentSourceRequest request, CancellationToken cancellationToken = default)
    {
        UpdateCalls.Add((id, request));
        return UpdateHandler(id, request);
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => DeleteHandler(id);
}
