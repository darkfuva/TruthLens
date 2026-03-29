using CodeHollow.FeedReader;
using TruthLens.Application.Services.Discovery;

namespace TruthLens.Infrastructure.Discovery;

public sealed class BingNewsSearchClient : INewsSearchClient
{
    private readonly HttpClient _httpClient;

    public BingNewsSearchClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<SearchNewsItem>> SearchArticleUrlsAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<SearchNewsItem>();
        }

        var requestUrl = $"https://www.bing.com/news/search?q={Uri.EscapeDataString(query)}&format=rss";

        using var response = await _httpClient.GetAsync(requestUrl, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        var feed = FeedReader.ReadFromString(xml);

        var rawItems = feed.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.Link))
            .Select(x => new SearchNewsItem(
                Url: x.Link!,
                Title: x.Title,
                PublishedAtUtc: x.PublishingDate is null
                    ? null
                    : new DateTimeOffset(DateTime.SpecifyKind(x.PublishingDate.Value, DateTimeKind.Utc))))
            .Take(maxResults)
            .ToList();

        var resolved = new List<SearchNewsItem>(rawItems.Count);
        foreach (var item in rawItems)
        {
            var publisherUrl = await ResolvePublisherUrlAsync(item.Url, ct);
            resolved.Add(item with { Url = publisherUrl });
        }

        return resolved;
    }

    private async Task<string> ResolvePublisherUrlAsync(string rawUrl, CancellationToken ct)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return rawUrl;
        }

        // Fast path for Bing wrapper links: parse query params to extract publisher URL.
        if (IsBingHost(uri.Host))
        {
            var query = ParseQuery(uri.Query);
            foreach (var key in new[] { "url", "u", "r" })
            {
                if (!query.TryGetValue(key, out var values))
                {
                    continue;
                }

                foreach (var value in values)
                {
                    var decoded = Uri.UnescapeDataString(value ?? string.Empty);
                    if (Uri.TryCreate(decoded, UriKind.Absolute, out var parsed) && !IsBingHost(parsed.Host))
                    {
                        return parsed.ToString();
                    }
                }
            }
        }

        // Fallback: follow redirects and use final URI host.
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var final = response.RequestMessage?.RequestUri;
            if (final is not null && final.IsAbsoluteUri)
            {
                return final.ToString();
            }
        }
        catch
        {
            // Keep the original URL if redirect resolution fails.
        }

        return rawUrl;
    }

    private static bool IsBingHost(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();
        return normalized == "bing.com" || normalized.EndsWith(".bing.com", StringComparison.Ordinal);
    }

    private static Dictionary<string, List<string>> ParseQuery(string query)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (!result.TryGetValue(key, out var list))
            {
                list = new List<string>();
                result[key] = list;
            }

            list.Add(value);
        }

        return result;
    }
}
