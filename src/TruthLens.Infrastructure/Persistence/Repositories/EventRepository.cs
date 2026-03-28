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

    public async Task<IReadOnlyList<Event>> GetRecentForDashboardAsync(int maxCount, CancellationToken ct)
    {
        return await _db.Events
            .AsNoTracking()
            .Include(e => e.Posts)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
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
}
