// TruthLens.Infrastructure/Embedding/OllamaEmbeddingClient.cs
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TruthLens.Application.Services.Embedding;
using TruthLens.Infrastructure.Ollama;

namespace TruthLens.Infrastructure.Embedding;

public sealed class OllamaEmbeddingClient : IEmbeddingClient
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaEmbeddingClient(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        if (texts.Count == 0) return Array.Empty<float[]>();

        var request = new EmbedRequest(_options.EmbeddingModel, texts);

        using var response = await _httpClient.PostAsJsonAsync("/api/embed", request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Ollama embed response was empty.");

        if (payload.Embeddings.Count != texts.Count)
            throw new InvalidOperationException("Embedding count mismatch.");

        return payload.Embeddings.Select(x => x.ToArray()).ToList();
    }

    private sealed record EmbedRequest(string Model, IReadOnlyList<string> Input);

    private sealed class EmbedResponse
    {
        public List<List<float>> Embeddings { get; set; } = new();
    }
}
