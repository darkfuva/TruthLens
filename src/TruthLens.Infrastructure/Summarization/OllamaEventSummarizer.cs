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
        var prompt = "Summarize the event in 3-5 factual sentences. Avoid speculation.\n\n" + context;

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
