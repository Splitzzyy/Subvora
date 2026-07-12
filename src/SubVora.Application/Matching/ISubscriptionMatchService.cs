namespace SubVora.Application.Matching;

public interface ISubscriptionMatchService
{
    Task<ResolveSubscriptionResponse> ResolveAsync(string freeTextInput, CancellationToken cancellationToken = default);
}
