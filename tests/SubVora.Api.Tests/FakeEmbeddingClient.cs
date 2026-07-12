using SubVora.Application.Matching;

namespace SubVora.Api.Tests;

/// <summary>Deterministic stand-in for OpenAiEmbeddingClient - Api.Tests never dial out to OpenAI.</summary>
public class FakeEmbeddingClient : IEmbeddingClient
{
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var vector = new float[1536];
        vector[0] = 1f;
        return Task.FromResult(vector);
    }
}
