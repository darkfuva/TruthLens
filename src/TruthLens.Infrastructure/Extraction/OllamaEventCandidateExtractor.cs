using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TruthLens.Application.Services.Extraction;
using TruthLens.Infrastructure.Ollama;

namespace TruthLens.Infrastructure.Extraction;

public sealed class OllamaEventCandidateExtractor : IEventCandidateExtractor
{
    private static readonly Regex LocationRegex = new(@"\bin\s+([A-Z][a-zA-Z]+(?:\s+[A-Z][a-zA-Z]+)*)", RegexOptions.Compiled);
    private static readonly Regex DateRegex = new(@"\b(?:\d{1,2}\s+[A-Za-z]{3,9}\s+\d{4}|\d{4}-\d{2}-\d{2}|[A-Za-z]{3,9}\s+\d{1,2},\s+\d{4})\b", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaEventCandidateExtractor(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<ExtractedEventCandidateDraft>> ExtractAsync(
        string postTitle,
        string? postSummary,
        CancellationToken ct)
    {
        var summary = postSummary ?? string.Empty;
        var prompt =
            "You extract real-world event candidates from one news post.\n" +
            "Return JSON only. No markdown.\n" +
            "Schema:\n" +
            "{\n" +
            "  \"candidates\": [\n" +
            "    {\n" +
            "      \"title\": \"short event title\",\n" +
            "      \"summary\": \"1 sentence factual summary\",\n" +
            "      \"timeHint\": \"optional time/window\",\n" +
            "      \"locationHint\": \"optional location\",\n" +
            "      \"actors\": [\"optional actor 1\", \"optional actor 2\"],\n" +
            "      \"confidence\": 0.0\n" +
            "    }\n" +
            "  ]\n" +
            "}\n\n" +
            "Rules:\n" +
            "- Prefer concrete occurrences, not broad themes.\n" +
            "- If uncertain, leave fields empty and lower confidence.\n" +
            "- Confidence between 0 and 1.\n\n" +
            $"Post title: {postTitle}\n" +
            $"Post summary: {summary}";

        try
        {
            var request = new
            {
                model = _options.ExtractionModel,
                messages = new[] { new { role = "user", content = prompt } },
                stream = false
            };

            using var response = await _httpClient.PostAsJsonAsync("/api/chat", request, ct);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
            var content = payload?.Message?.Content?.Trim() ?? string.Empty;

            var parsed = TryParse(content);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }
        catch
        {
            // Fall back to deterministic extraction below.
        }

        return BuildFallback(postTitle, summary);
    }

    private static IReadOnlyList<ExtractedEventCandidateDraft> TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ExtractedEventCandidateDraft>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<ExtractedEventCandidateDraft>();
            }

            var results = new List<ExtractedEventCandidateDraft>();
            foreach (var item in candidates.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var summary = item.TryGetProperty("summary", out var summaryElement) ? summaryElement.GetString() : null;
                var timeHint = item.TryGetProperty("timeHint", out var timeElement) ? timeElement.GetString() : null;
                var locationHint = item.TryGetProperty("locationHint", out var locationElement) ? locationElement.GetString() : null;
                var confidence = item.TryGetProperty("confidence", out var confidenceElement) && confidenceElement.TryGetDouble(out var parsedConfidence)
                    ? Math.Max(0, Math.Min(1, parsedConfidence))
                    : 0.5;

                var actors = new List<string>();
                if (item.TryGetProperty("actors", out var actorArray) && actorArray.ValueKind == JsonValueKind.Array)
                {
                    actors.AddRange(actorArray.EnumerateArray()
                        .Select(x => x.GetString()?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))!);
                }

                results.Add(new ExtractedEventCandidateDraft(
                    Title: title.Trim(),
                    Summary: string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
                    TimeHint: string.IsNullOrWhiteSpace(timeHint) ? null : timeHint.Trim(),
                    LocationHint: string.IsNullOrWhiteSpace(locationHint) ? null : locationHint.Trim(),
                    Actors: actors,
                    Confidence: confidence,
                    Source: "ollama"));
            }

            return results;
        }
        catch
        {
            return Array.Empty<ExtractedEventCandidateDraft>();
        }
    }

    private static IReadOnlyList<ExtractedEventCandidateDraft> BuildFallback(string title, string summary)
    {
        var sourceText = $"{title} {summary}".Trim();
        var timeHint = DateRegex.Match(sourceText).Success ? DateRegex.Match(sourceText).Value : null;
        var locationHint = LocationRegex.Match(sourceText).Success ? LocationRegex.Match(sourceText).Groups[1].Value : null;

        var actors = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length > 2 && char.IsUpper(x[0]))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return
        [
            new ExtractedEventCandidateDraft(
                Title: title,
                Summary: string.IsNullOrWhiteSpace(summary) ? null : summary,
                TimeHint: timeHint,
                LocationHint: locationHint,
                Actors: actors,
                Confidence: 0.55,
                Source: "fallback")
        ];
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
