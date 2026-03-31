// IEventRepository.cs

namespace TruthLens.Application.Repositories.Event;

using Pgvector;
using TruthLens.Domain.Entities;

public interface IEventRepository
{
    Task<IReadOnlyList<Event>> GetRecentWithCentroidAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct);
    Task<IReadOnlyList<Event>> GetByIdsWithGraphAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);
    Task<int> CountForDashboardAsync(double? minConfidence, bool includeProvisional, CancellationToken ct);
    Task<IReadOnlyList<Event>> GetPageForDashboardAsync(int page, int pageSize, string sort, double? minConfidence, bool includeProvisional, CancellationToken ct);
    Task<Event> CreateAsync(string title, Vector centroidEmbedding, DateTimeOffset seenAtUtc, CancellationToken ct);
    Task RecomputeCentroidsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);
    Task RecomputeCentroidsFromLinksAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);

    Task<IReadOnlyList<Event>> GetUnsummarizedBatchAsync(int batchSize, CancellationToken ct);
    Task<IReadOnlyList<Post>> GetRecentPostsForEventAsync(Guid eventId, int maxPosts, CancellationToken ct);
    Task<IReadOnlyList<(Post Post, PostEventLink Link)>> GetRecentLinkedPostsForEventAsync(Guid eventId, int maxPosts, CancellationToken ct);
    Task<IReadOnlyList<ExternalEvidencePost>> GetRecentExternalEvidenceForEventAsync(Guid eventId, int maxItems, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    Task<IReadOnlyList<Event>> GetRecentForConfidenceScoringAsync(int maxCount, DateTimeOffset sinceUtc, CancellationToken ct);
    Task<IReadOnlyList<Event>> GetRecentForSourceDiscoveryAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct);
    Task<IReadOnlyList<Event>> GetRecentForRelationRecomputeAsync(IReadOnlyCollection<Guid> touchedEventIds, DateTimeOffset sinceUtc, int maxCount, CancellationToken ct);

}
