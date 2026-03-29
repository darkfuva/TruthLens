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
        var recentPosts = _dbContext.Posts
            .Where(p => p.SourceId == sourceId && p.PublishedAtUtc >= sinceUtc);

        var recentPostCount = await recentPosts.CountAsync(ct);

        var corroboratedRecentPostCount = await recentPosts
            .Where(p => p.EventId != null)
            .CountAsync(p =>
                _dbContext.Posts.Any(other =>
                    other.EventId == p.EventId &&
                    other.SourceId != sourceId), ct);

        var averageClusterAssignmentScore = await recentPosts
            .Where(p => p.ClusterAssignmentScore != null)
            .Select(p => p.ClusterAssignmentScore)
            .AverageAsync(ct);

        var latestPublishedAtUtc = await recentPosts
            .Select(p => (DateTimeOffset?)p.PublishedAtUtc)
            .MaxAsync(ct);

        return new SourceScoringStats(
            recentPostCount,
            corroboratedRecentPostCount,
            averageClusterAssignmentScore,
            latestPublishedAtUtc);
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
        var recentPosts = _dbContext.Posts
            .Where(p => sourceIdSet.Contains(p.SourceId) && p.PublishedAtUtc >= sinceUtc);

        var aggregated = await recentPosts
            .Select(p => new
            {
                p.SourceId,
                p.PublishedAtUtc,
                p.ClusterAssignmentScore,
                IsCorroborated = p.EventId != null && _dbContext.Posts.Any(other =>
                    other.EventId == p.EventId &&
                    other.SourceId != p.SourceId)
            })
            .GroupBy(x => x.SourceId)
            .Select(g => new
            {
                SourceId = g.Key,
                RecentPostCount = g.Count(),
                CorroboratedRecentPostCount = g.Count(x => x.IsCorroborated),
                AverageClusterAssignmentScore = g.Average(x => x.ClusterAssignmentScore),
                LatestPublishedAtUtc = g.Max(x => (DateTimeOffset?)x.PublishedAtUtc)
            })
            .ToListAsync(ct);

        var map = aggregated.ToDictionary(
            x => x.SourceId,
            x => new SourceScoringStats(
                x.RecentPostCount,
                x.CorroboratedRecentPostCount,
                x.AverageClusterAssignmentScore,
                x.LatestPublishedAtUtc));

        foreach (var sourceId in sourceIds)
        {
            if (!map.ContainsKey(sourceId))
            {
                map[sourceId] = new SourceScoringStats(
                    RecentPostCount: 0,
                    CorroboratedRecentPostCount: 0,
                    AverageClusterAssignmentScore: null,
                    LatestPublishedAtUtc: null);
            }
        }

        return map;
    }

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
