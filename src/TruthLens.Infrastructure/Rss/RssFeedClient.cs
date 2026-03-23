// src/TruthLens.Infrastructure/Rss/RssFeedClient.cs
using CodeHollow.FeedReader;
using TruthLens.Application.DTOs;
using TruthLens.Application.Services.Rss;

namespace TruthLens.Infrastructure.Rss;

public sealed class RssFeedClient : IRssFeedClient
{
    private readonly HttpClient _httpClient;

    public RssFeedClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<FeedItemDto>> ReadAsync(string feedUrl, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(feedUrl, ct);
        response.EnsureSuccessStatusCode();

        var xml = await response.Content.ReadAsStringAsync(ct);
        var feed = FeedReader.ReadFromString(xml);

        var items = feed.Items
            .Select(item => new FeedItemDto(
                ExternalId: string.IsNullOrWhiteSpace(item.Id) ? (item.Link ?? Guid.NewGuid().ToString()) : item.Id,
                Title: item.Title ?? "(untitled)",
                Url: item.Link ?? string.Empty,
                Summary: item.Description,
                PublishedAtUtc: (item.PublishingDate ?? DateTime.UtcNow).ToUniversalTime()
            ))
            .Where(x => !string.IsNullOrWhiteSpace(x.Url))
            .ToList();

        return items;
    }
}
