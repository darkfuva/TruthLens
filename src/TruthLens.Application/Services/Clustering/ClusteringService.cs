// ClusteringService.cs
using TruthLens.Application.Repositories.Event;
using TruthLens.Application.Services.Post;

namespace TruthLens.Application.Services.Clustering;

public sealed class ClusteringService
{
    private readonly IPostRepository _postRepository;
    private readonly IEventRepository _eventRepository;
    private readonly ICosineSimilarityService _similarityService;

    public ClusteringService(
        IPostRepository postRepository,
        IEventRepository eventRepository,
        ICosineSimilarityService similarityService)
    {
        _postRepository = postRepository;
        _eventRepository = eventRepository;
        _similarityService = similarityService;
    }

    public async Task<int> ClusterPendingPostsAsync(int batchSize, double threshold, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var posts = await _postRepository.GetUnclusteredEmbeddedBatchAsync(batchSize, ct);
        var touchedEventIds = new HashSet<Guid>();
        if (posts.Count == 0) return 0;

        var candidates = (await _eventRepository.GetRecentWithCentroidAsync(now.AddDays(-7), 500, ct))
            .Where(e => e.CentroidEmbedding is not null)
            .ToList();

        var clustered = 0;

        foreach (var post in posts)
        {
            if (post.Embedding is null) continue;

            TruthLens.Domain.Entities.Event? bestEvent = null;
            var bestScore = double.MinValue;

            foreach (var evt in candidates)
            {
                var score = _similarityService.Calculate(post.Embedding, evt.CentroidEmbedding!);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEvent = evt;
                }
            }

            if (bestEvent is not null && bestScore >= threshold)
            {
                post.EventId = bestEvent.Id;
                post.ClusterAssignmentScore = bestScore;
                bestEvent.LastSeenAtUtc = now;
                touchedEventIds.Add(bestEvent.Id);
            }
            else
            {
                var newEvent = await _eventRepository.CreateAsync(post.Title, post.Embedding, now, ct);
                post.EventId = newEvent.Id;
                post.ClusterAssignmentScore = 1.0;
                candidates.Add(newEvent);
                touchedEventIds.Add(newEvent.Id);
            }

            clustered++;
        }

        await _postRepository.SaveChangesAsync(ct);
        await _eventRepository.RecomputeCentroidsAsync(touchedEventIds, ct);
        await _postRepository.SaveChangesAsync(ct);
        return clustered;
    }
}
