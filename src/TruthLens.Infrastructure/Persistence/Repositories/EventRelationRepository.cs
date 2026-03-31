using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class EventRelationRepository : IEventRelationRepository
{
    private readonly TruthLensDbContext _db;

    public EventRelationRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<EventRelation>> GetForEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return Array.Empty<EventRelation>();
        }

        return await _db.EventRelations
            .Where(x => eventIds.Contains(x.FromEventId) || eventIds.Contains(x.ToEventId))
            .ToListAsync(ct);
    }

    public async Task UpsertRangeAsync(IEnumerable<EventRelation> relations, CancellationToken ct)
    {
        foreach (var relation in relations)
        {
            var existing = await _db.EventRelations
                .FirstOrDefaultAsync(x =>
                    x.FromEventId == relation.FromEventId &&
                    x.ToEventId == relation.ToEventId &&
                    x.RelationType == relation.RelationType, ct);

            if (existing is null)
            {
                await _db.EventRelations.AddAsync(relation, ct);
                continue;
            }

            existing.Strength = relation.Strength;
            existing.EvidenceCount = relation.EvidenceCount;
            existing.UpdatedAtUtc = relation.UpdatedAtUtc;
        }
    }

    public async Task RemoveWeakOrStaleForEventIdsAsync(
        IReadOnlyCollection<Guid> eventIds,
        double minStrength,
        DateTimeOffset minUpdatedAtUtc,
        CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var stale = await _db.EventRelations
            .Where(x =>
                (eventIds.Contains(x.FromEventId) || eventIds.Contains(x.ToEventId)) &&
                (x.Strength < minStrength || x.UpdatedAtUtc < minUpdatedAtUtc))
            .ToListAsync(ct);

        if (stale.Count == 0)
        {
            return;
        }

        _db.EventRelations.RemoveRange(stale);
    }
}
