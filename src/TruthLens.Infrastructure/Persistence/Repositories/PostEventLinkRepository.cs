using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class PostEventLinkRepository : IPostEventLinkRepository
{
    private readonly TruthLensDbContext _db;

    public PostEventLinkRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public Task AddRangeAsync(IEnumerable<PostEventLink> links, CancellationToken ct)
    {
        return _db.PostEventLinks.AddRangeAsync(links, ct);
    }

    public async Task<IReadOnlyList<PostEventLink>> GetForEventIdsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return Array.Empty<PostEventLink>();
        }

        return await _db.PostEventLinks
            .Include(x => x.Post)
            .ThenInclude(p => p.Source)
            .Where(x => eventIds.Contains(x.EventId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PostEventLink>> GetForPostIdsAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct)
    {
        if (postIds.Count == 0)
        {
            return Array.Empty<PostEventLink>();
        }

        return await _db.PostEventLinks
            .Where(x => postIds.Contains(x.PostId))
            .ToListAsync(ct);
    }
}
