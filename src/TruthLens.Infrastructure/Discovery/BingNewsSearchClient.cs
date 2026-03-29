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

    public async Task<IReadOnlyList<string>> SearchArticleUrlsAsync(string query, int maxResults, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var requestUrl = $"https://www.bing.com/news/search?q={Uri.EscapeDataString(query)}&format=rss";

        using var response = await _httpClient.GetAsync(requestUrl, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        var feed = FeedReader.ReadFromString(xml);

        return feed.Items
            .Select(x => x.Link)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(maxResults)
            .Select(x => x!)
            .ToList();
    }
}
