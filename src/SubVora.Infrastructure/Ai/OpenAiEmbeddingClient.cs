using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SubVora.Application.Matching;

namespace SubVora.Infrastructure.Ai;

/// <summary>Typed HttpClient wrapper over OpenAI's /embeddings endpoint - same shape as ExchangeRateHostClient, no SDK dependency.</summary>
public class OpenAiEmbeddingClient : IEmbeddingClient
{
    private const string Model = "text-embedding-3-small";

    private readonly HttpClient _httpClient;

    public OpenAiEmbeddingClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("embeddings", new OpenAiEmbeddingRequest(Model, text), cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAiEmbeddingResponse>(cancellationToken: cancellationToken);
        var embedding = payload?.Data?.FirstOrDefault()?.Embedding;

        return embedding is null or { Length: 0 }
            ? throw new InvalidOperationException("OpenAI embeddings response did not contain an embedding vector.")
            : embedding;
    }

    private record OpenAiEmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] string Input);

    private class OpenAiEmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiEmbeddingDatum>? Data { get; set; }
    }

    private class OpenAiEmbeddingDatum
    {
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = [];
    }
}
