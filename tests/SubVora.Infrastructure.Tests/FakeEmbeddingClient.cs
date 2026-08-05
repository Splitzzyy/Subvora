using SubVora.Application.Matching;

namespace SubVora.Infrastructure.Tests;

/// <summary>
/// Deterministic stand-in for OpenAiEmbeddingClient - Infrastructure.Tests never dial out to
/// OpenAI. Mirrors the fake in SubVora.Api.Tests (the test projects share no code), with two
/// additions this slice needs: a call counter, so "the backfill re-embedded nothing" is assertable,
/// and a character-frequency vector rather than a constant one, so two similar provider names come
/// out cosine-close and a near-miss lookup can actually be exercised end to end.
/// </summary>
public class FakeEmbeddingClient : IEmbeddingClient
{
    public int CallCount { get; private set; }

    public List<string> EmbeddedTexts { get; } = [];

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        EmbeddedTexts.Add(text);
        return Task.FromResult(Embed(text));
    }

    public static float[] Embed(string text)
    {
        var vector = new float[1536];
        foreach (var character in text.ToLowerInvariant())
        {
            if (character < vector.Length)
            {
                vector[character] += 1f;
            }
        }

        return vector;
    }
}
