// Persistence/Repositories/PostRepository.cs
using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Post;
using TruthLens.Domain.Entities;
using TruthLens.Infrastructure.Persistence;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class PostRepository : IPostRepository
{
    private readonly TruthLensDbContext _db;

    public PostRepository(TruthLensDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid sourceId, string externalId, CancellationToken ct)
    {
        return _db.Posts.AnyAsync(x => x.SourceId == sourceId && x.ExternalId == externalId, ct);
    }

    public Task AddRangeAsync(IEnumerable<Post> posts, CancellationToken ct)
    {
        return _db.Posts.AddRangeAsync(posts, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct)
    {
        return _db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(IReadOnlyCollection<Guid> postIds, CancellationToken ct)
    {
        if (postIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await _db.Posts
            .Where(x => postIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<Post>> GetUnembeddedBatchAsync(int batchSize, CancellationToken ct)
    {
        // tracked query because we will update entities and call SaveChangesAsync
        return await _db.Posts
            .Where(x => x.Embedding == null)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<Post>> GetUnclusteredEmbeddedBatchAsync(int batchSize, CancellationToken ct)
    {
        return await _db.Posts
            .Where(p => p.EventId == null && p.Embedding != null)
            .OrderBy(p => p.PublishedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Post>> GetEmbeddedWithoutPrimaryLinkBatchAsync(int batchSize, DateTimeOffset? sinceUtc, CancellationToken ct)
    {
        var query = _db.Posts
            .Include(x => x.Source)
            .Where(x => x.Embedding != null && !x.EventLinks.Any(l => l.IsPrimary));

        if (sinceUtc.HasValue)
        {
            query = query.Where(x => x.PublishedAtUtc >= sinceUtc.Value);
        }

        return await query
            .OrderByDescending(x => x.PublishedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }
}
