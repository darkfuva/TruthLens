// OllamaEventSummarizer.cs
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TruthLens.Application.Services.Summarization;
using TruthLens.Infrastructure.Ollama;

namespace TruthLens.Infrastructure.Summarization;

public sealed class OllamaEventSummarizer : IEventSummarizer
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaEventSummarizer(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> SummarizeAsync(string context, CancellationToken ct)
    {
        var prompt = """
                     You are an evidence-grounded news analyst.
                     Write a factual event summary using ONLY supplied evidence.

                     Output rules:
                     - 4 to 6 sentences.
                     - No speculation, no invented facts.
                     - Mention uncertainty explicitly when evidence is incomplete/conflicting.
                     - If an important detail is missing, say "unknown".
                     - Keep neutral tone.

                     Evidence bundle:
                     """
                     + "\n\n" + context;

        var request = new
        {
            model = _options.SummarizationModel,
            messages = new[] { new { role = "user", content = prompt } },
            stream = false
        };

        using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, ct);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty Ollama response.");

        return payload.Message?.Content?.Trim() ?? throw new InvalidOperationException("Empty summary.");
    }

    private sealed class OllamaChatResponse
    {
        public OllamaMessage? Message { get; set; }
    }

    private sealed class OllamaMessage
    {
        public string? Content { get; set; }
    }
}
