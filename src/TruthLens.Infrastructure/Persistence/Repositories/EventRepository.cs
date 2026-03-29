// EventRepository.cs
using Microsoft.EntityFrameworkCore;
using Pgvector;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class EventRepository : IEventRepository
{
    private readonly TruthLensDbContext _db;

    public EventRepository(TruthLensDbContext db) => _db = db;

    public async Task<IReadOnlyList<Event>> GetRecentWithCentroidAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct)
    {
        return await _db.Events
            .Where(e => e.LastSeenAtUtc >= sinceUtc && e.CentroidEmbedding != null)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<int> CountForDashboardAsync(double? minConfidence, bool includeProvisional, CancellationToken ct)
    {
        return await BuildDashboardBaseQuery(minConfidence, includeProvisional).CountAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetPageForDashboardAsync(
        int page,
        int pageSize,
        string sort,
        double? minConfidence,
        bool includeProvisional,
        CancellationToken ct)
    {
        var skip = (page - 1) * pageSize;
        IQueryable<Event> query = BuildDashboardBaseQuery(minConfidence, includeProvisional)
            .Include(e => e.Posts)
            .Include(e => e.ExternalEvidencePosts);

        query = sort == "confidence"
            ? query
                .OrderByDescending(e => e.ConfidenceScore ?? -1)
                .ThenByDescending(e => e.LastSeenAtUtc)
                .ThenBy(e => e.Id)
            : query
                .OrderByDescending(e => e.LastSeenAtUtc)
                .ThenBy(e => e.Id);

        return await query
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Event> CreateAsync(string title, Vector centroidEmbedding, DateTimeOffset seenAtUtc, CancellationToken ct)
    {
        var evt = new Event
        {
            Id = Guid.NewGuid(),
            Title = title,
            CentroidEmbedding = centroidEmbedding,
            FirstSeenAtUtc = seenAtUtc,
            LastSeenAtUtc = seenAtUtc
        };

        await _db.Events.AddAsync(evt, ct);
        return evt;
    }
    public async Task RecomputeCentroidsAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0) return;

        var events = await _db.Events
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(ct);

        foreach (var evt in events)
        {
            var vectors = await _db.Posts
                .Where(p => p.EventId == evt.Id && p.Embedding != null)
                .Select(p => p.Embedding!)
                .ToListAsync(ct);

            if (vectors.Count == 0) continue;

            evt.CentroidEmbedding = ComputeCentroid(vectors);
        }
    }
    private static Vector ComputeCentroid(IReadOnlyList<Vector> vectors)
    {
        var first = vectors[0].ToArray();
        var dim = first.Length;
        var sums = new double[dim];

        foreach (var v in vectors)
        {
            var values = v.ToArray();
            if (values.Length != dim)
                throw new InvalidOperationException("Embedding dimensions mismatch.");

            for (var i = 0; i < dim; i++)
                sums[i] += values[i];
        }

        var centroid = new float[dim];
        for (var i = 0; i < dim; i++)
            centroid[i] = (float)(sums[i] / vectors.Count);

        return new Vector(centroid);
    }
    public async Task<IReadOnlyList<Event>> GetUnsummarizedBatchAsync(int batchSize, CancellationToken ct)
    {
        return await _db.Events
            .Where(e => e.Summary == null)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Post>> GetRecentPostsForEventAsync(Guid eventId, int maxPosts, CancellationToken ct)
    {
        return await _db.Posts
            .Where(p => p.EventId == eventId)
            .OrderByDescending(p => p.PublishedAtUtc)
            .Take(maxPosts)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    public async Task<IReadOnlyList<Event>> GetRecentForConfidenceScoringAsync(int maxCount, DateTimeOffset sinceUtc, CancellationToken ct)
    {
        return await _db.Events
            .AsSplitQuery()
            .Include(e => e.Posts)
            .ThenInclude(p => p.Source)
            .Include(e => e.ExternalEvidencePosts)
            .ThenInclude(x => x.ExternalSource)
            .Where(e =>
                e.Posts.Any() &&
                (e.LastSeenAtUtc >= sinceUtc || e.ConfidenceScore == null || e.Status == "provisional"))
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetRecentForSourceDiscoveryAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct)
    {
        return await _db.Events
            .Include(e => e.Posts)
            .Where(e => e.LastSeenAtUtc >= sinceUtc && e.Posts.Any())
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    private IQueryable<Event> BuildDashboardBaseQuery(double? minConfidence, bool includeProvisional)
    {
        var query = _db.Events.AsNoTracking().AsQueryable();

        if (!includeProvisional)
        {
            query = query.Where(e => e.Status == "confirmed");
        }

        if (minConfidence.HasValue)
        {
            query = query.Where(e => e.ConfidenceScore != null && e.ConfidenceScore >= minConfidence.Value);
        }

        return query;
    }
}
