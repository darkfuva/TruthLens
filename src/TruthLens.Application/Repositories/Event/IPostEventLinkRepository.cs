using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.Event;

public interface IPostEventLinkRepository
{
    Task AddRangeAsync(IEnumerable<PostEventLink> links, CancellationToken ct);
    Task<IReadOnlyList<PostEventLink>> GetForEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);
    Task<IReadOnlyList<PostEventLink>> GetForPostIdsAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct);
}
