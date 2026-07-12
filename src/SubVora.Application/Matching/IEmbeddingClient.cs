namespace SubVora.Application.Matching;

public interface IEmbeddingClient
{
    Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}
