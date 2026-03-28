// IEventRepository.cs

namespace TruthLens.Application.Repositories.Event;

using Pgvector;
using TruthLens.Domain.Entities;

public interface IEventRepository
{
    Task<IReadOnlyList<Event>> GetRecentWithCentroidAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct);
    Task<Event> CreateAsync(string title, Vector centroidEmbedding, DateTimeOffset seenAtUtc, CancellationToken ct);
    Task RecomputeCentroidsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);

    Task<IReadOnlyList<Event>> GetUnsummarizedBatchAsync(int batchSize, CancellationToken ct);
    Task<IReadOnlyList<Post>> GetRecentPostsForEventAsync(Guid eventId, int maxPosts, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);

}
