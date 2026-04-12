namespace TruthLens.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Source;
using TruthLens.Domain.Entities;

public class SourceRepository : ISourceRepository
{

    private readonly TruthLensDbContext _dbContext;
    public SourceRepository(TruthLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }
    public async Task<IReadOnlyList<Source>> GetActiveAsync(CancellationToken ct)
    {
        return await _dbContext.Sources.Where(source => source.IsActive).AsNoTracking().ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Source>> GetActiveForScoringAsync(int maxCount, CancellationToken ct)
    {
        return await _dbContext.Sources
            .Where(source => source.IsActive)
            .OrderByDescending(x => x.ConfidenceUpdatedAtUtc ?? DateTimeOffset.MinValue)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetAllFeedUrlsAsync(CancellationToken ct)
    {
        return await _dbContext.Sources
            .AsNoTracking()
            .Select(x => x.FeedUrl)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToListAsync(ct);
    }

    public async Task<SourceScoringStats> GetScoringStatsAsync(Guid sourceId, DateTimeOffset sinceUtc, CancellationToken ct)
    {
        var statsMap = await GetScoringStatsMapAsync(new[] { sourceId }, sinceUtc, ct);
        return statsMap.TryGetValue(sourceId, out var stats)
            ? stats
            : new SourceScoringStats(0, 0, null, null);
    }

    public async Task<IReadOnlyDictionary<Guid, SourceScoringStats>> GetScoringStatsMapAsync(
        IReadOnlyCollection<Guid> sourceIds,
        DateTimeOffset sinceUtc,
        CancellationToken ct)
    {
        if (sourceIds.Count == 0)
        {
            return new Dictionary<Guid, SourceScoringStats>();
        }

        var sourceIdSet = sourceIds.ToHashSet();
        var recentPosts = await _dbContext.Posts
            .AsNoTracking()
            .Where(p => sourceIdSet.Contains(p.SourceId) && p.PublishedAtUtc >= sinceUtc)
            .Select(p => new RecentPostProjection(
                p.Id,
                p.SourceId,
                p.PublishedAtUtc,
                p.EventLinks
                    .Where(l => l.IsPrimary)
                    .Select(l => (double?)l.RelevanceScore)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        var corroboratedPostIds = (await _dbContext.PostEventLinks
            .AsNoTracking()
            .Where(l => sourceIdSet.Contains(l.Post.SourceId) && l.Post.PublishedAtUtc >= sinceUtc)
            .Where(l => _dbContext.PostEventLinks.Any(other =>
                other.EventId == l.EventId &&
                other.Post.SourceId != l.Post.SourceId))
            .Select(l => l.PostId)
            .Distinct()
            .ToListAsync(ct))
            .ToHashSet();

        var map = recentPosts
            .GroupBy(x => x.SourceId)
            .ToDictionary(
                g => g.Key,
                g => new SourceScoringStats(
                    RecentPostCount: g.Count(),
                    CorroboratedRecentPostCount: g.Count(x => corroboratedPostIds.Contains(x.PostId)),
                    AveragePrimaryLinkRelevanceScore: AverageNullable(g.Select(x => x.PrimaryRelevanceScore)),
                    LatestPublishedAtUtc: g.Max(x => (DateTimeOffset?)x.PublishedAtUtc)));

        foreach (var sourceId in sourceIds)
        {
            if (!map.ContainsKey(sourceId))
            {
                map[sourceId] = new SourceScoringStats(
                    RecentPostCount: 0,
                    CorroboratedRecentPostCount: 0,
                    AveragePrimaryLinkRelevanceScore: null,
                    LatestPublishedAtUtc: null);
            }
        }

        return map;
    }

    private static double? AverageNullable(IEnumerable<double?> values)
    {
        var concrete = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return concrete.Count == 0 ? null : concrete.Average();
    }

    private sealed record RecentPostProjection(
        Guid PostId,
        Guid SourceId,
        DateTimeOffset PublishedAtUtc,
        double? PrimaryRelevanceScore);

    public Task<bool> ExistsByFeedUrlAsync(string feedUrl, CancellationToken ct)
    {
        var normalized = feedUrl.Trim();
        return _dbContext.Sources.AnyAsync(x => x.FeedUrl == normalized, ct);
    }

    public Task AddAsync(Source source, CancellationToken ct) =>
        _dbContext.Sources.AddAsync(source, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct) =>
        _dbContext.SaveChangesAsync(ct);
}
