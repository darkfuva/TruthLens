using TruthLens.Domain.Entities;

namespace TruthLens.Application.Repositories.Event;

public interface IEventRelationRepository
{
    Task<IReadOnlyList<EventRelation>> GetForEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct);
    Task UpsertRangeAsync(IEnumerable<EventRelation> relations, CancellationToken ct);
    Task RemoveWeakOrStaleForEventIdsAsync(
        IReadOnlyCollection<Guid> eventIds,
        double minStrength,
        DateTimeOffset minUpdatedAtUtc,
        CancellationToken ct);
}
