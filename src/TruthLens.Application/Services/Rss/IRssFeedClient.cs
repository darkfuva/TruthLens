using TruthLens.Application.DTOs;

namespace TruthLens.Application.Services.Rss;

public interface IRssFeedClient
{
    Task<IReadOnlyList<FeedItemDto>> ReadAsync(string feedUrl, CancellationToken ct);
}
