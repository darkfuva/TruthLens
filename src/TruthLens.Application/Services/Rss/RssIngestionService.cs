namespace TruthLens.Application.Services.Rss;

using TruthLens.Application.Repositories.Post;
using TruthLens.Application.Repositories.Source;
using TruthLens.Domain.Entities;

public sealed class RssIngestionService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IPostRepository _postRepository;
    private readonly IRssFeedClient _rssFeedClient;

    public RssIngestionService(
        ISourceRepository sourceRepository,
        IPostRepository postRepository,
        IRssFeedClient rssFeedClient)
    {
        _sourceRepository = sourceRepository;
        _postRepository = postRepository;
        _rssFeedClient = rssFeedClient;
    }

    public async Task<int> IngestAllAsync(CancellationToken ct)
    {
        var sources = await _sourceRepository.GetActiveAsync(ct);
        var newPosts = new List<Post>();

        foreach (var source in sources)
        {
            var items = await _rssFeedClient.ReadAsync(source.FeedUrl, ct);

            foreach (var item in items)
            {
                var exists = await _postRepository.ExistsAsync(source.Id, item.ExternalId, ct);
                if (exists) continue;

                newPosts.Add(new Post
                {
                    Id = Guid.NewGuid(),
                    SourceId = source.Id,
                    ExternalId = item.ExternalId,
                    Title = item.Title,
                    Url = item.Url,
                    Summary = item.Summary,
                    PublishedAtUtc = item.PublishedAtUtc,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                });
            }
        }

        if (newPosts.Count > 0)
        {
            await _postRepository.AddRangeAsync(newPosts, ct);
            await _postRepository.SaveChangesAsync(ct);
        }

        return newPosts.Count;
    }
}
