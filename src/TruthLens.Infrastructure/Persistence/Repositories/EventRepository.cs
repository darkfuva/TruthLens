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
            .Where(e => e.Status != "merged" && e.LastSeenAtUtc >= sinceUtc && e.CentroidEmbedding != null)
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetByIdsWithGraphAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return Array.Empty<Event>();
        }

        return await _db.Events
            .AsSplitQuery()
            .Include(e => e.PostLinks)
            .ThenInclude(l => l.Post)
            .ThenInclude(p => p.Source)
            .Include(e => e.ExternalEvidencePosts)
            .ThenInclude(x => x.ExternalSource)
            .Include(e => e.OutgoingRelations)
            .Include(e => e.IncomingRelations)
            .Where(e => eventIds.Contains(e.Id))
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
            .AsSplitQuery()
            .Include(e => e.PostLinks)
            .ThenInclude(l => l.Post)
            .ThenInclude(p => p.Source)
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
    public async Task RecomputeCentroidsFromLinksAsync(IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        if (eventIds.Count == 0)
        {
            return;
        }

        var events = await _db.Events
            .Where(e => eventIds.Contains(e.Id))
            .ToListAsync(ct);

        var links = await _db.PostEventLinks
            .Include(x => x.Post)
            .Where(x => eventIds.Contains(x.EventId) && x.Post.Embedding != null)
            .ToListAsync(ct);

        var linksByEvent = links
            .GroupBy(x => x.EventId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var evt in events)
        {
            if (!linksByEvent.TryGetValue(evt.Id, out var eventLinks) || eventLinks.Count == 0)
            {
                continue;
            }

            evt.CentroidEmbedding = ComputeWeightedCentroid(eventLinks);
        }
    }
    public async Task<IReadOnlyList<Event>> GetUnsummarizedBatchAsync(int batchSize, CancellationToken ct)
    {
        return await _db.Events
            .Where(e => e.Status != "merged" && (e.Summary == null || e.SummaryModel == null || e.SummarizedAtUtc == null))
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(Post Post, PostEventLink Link)>> GetRecentLinkedPostsForEventAsync(Guid eventId, int maxPosts, CancellationToken ct)
    {
        var links = await _db.PostEventLinks
            .Include(x => x.Post)
            .ThenInclude(p => p.Source)
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.IsPrimary)
            .ThenByDescending(x => x.RelevanceScore)
            .ThenByDescending(x => x.Post.PublishedAtUtc)
            .Take(maxPosts)
            .ToListAsync(ct);

        return links.Select(x => (x.Post, x)).ToList();
    }

    public async Task<IReadOnlyList<ExternalEvidencePost>> GetRecentExternalEvidenceForEventAsync(Guid eventId, int maxItems, CancellationToken ct)
    {
        return await _db.ExternalEvidencePosts
            .Include(x => x.ExternalSource)
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.RelevanceScore)
            .ThenByDescending(x => x.DiscoveredAtUtc)
            .Take(maxItems)
            .ToListAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);
    public async Task<IReadOnlyList<Event>> GetRecentForConfidenceScoringAsync(int maxCount, DateTimeOffset sinceUtc, CancellationToken ct)
    {
        return await _db.Events
            .AsSplitQuery()
            .Include(e => e.PostLinks)
            .ThenInclude(x => x.Post)
            .ThenInclude(p => p.Source)
            .Include(e => e.ExternalEvidencePosts)
            .ThenInclude(x => x.ExternalSource)
            .Where(e =>
                e.Status != "merged" &&
                e.PostLinks.Any() &&
                (e.LastSeenAtUtc >= sinceUtc || e.ConfidenceScore == null || e.Status == "provisional"))
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetRecentForSourceDiscoveryAsync(DateTimeOffset sinceUtc, int maxCount, CancellationToken ct)
    {
        return await _db.Events
            .AsSplitQuery()
            .Include(e => e.PostLinks)
            .ThenInclude(x => x.Post)
            .Where(e => e.Status != "merged" && e.LastSeenAtUtc >= sinceUtc && e.PostLinks.Any())
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Event>> GetRecentForRelationRecomputeAsync(
        IReadOnlyCollection<Guid> touchedEventIds,
        DateTimeOffset sinceUtc,
        int maxCount,
        CancellationToken ct)
    {
        if (touchedEventIds.Count == 0)
        {
            return Array.Empty<Event>();
        }

        return await _db.Events
            .Where(e =>
                e.Status != "merged" &&
                e.CentroidEmbedding != null &&
                (touchedEventIds.Contains(e.Id) || e.LastSeenAtUtc >= sinceUtc))
            .OrderByDescending(e => e.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    private IQueryable<Event> BuildDashboardBaseQuery(double? minConfidence, bool includeProvisional)
    {
        var query = _db.Events
            .AsNoTracking()
            .Where(e => e.Status != "merged")
            .AsQueryable();

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

    private static Vector ComputeWeightedCentroid(IReadOnlyList<PostEventLink> links)
    {
        var first = links[0].Post.Embedding!.ToArray();
        var dim = first.Length;
        var sums = new double[dim];
        double weightSum = 0;

        foreach (var link in links)
        {
            var embedding = link.Post.Embedding;
            if (embedding is null)
            {
                continue;
            }

            var values = embedding.ToArray();
            if (values.Length != dim)
            {
                continue;
            }

            var weight = Math.Max(0.05, link.RelevanceScore) * (link.IsPrimary ? 1.0 : 0.6);
            weightSum += weight;

            for (var i = 0; i < dim; i++)
            {
                sums[i] += values[i] * weight;
            }
        }

        if (weightSum <= 0)
        {
            return new Vector(first);
        }

        var centroid = new float[dim];
        for (var i = 0; i < dim; i++)
        {
            centroid[i] = (float)(sums[i] / weightSum);
        }

        return new Vector(centroid);
    }
}
