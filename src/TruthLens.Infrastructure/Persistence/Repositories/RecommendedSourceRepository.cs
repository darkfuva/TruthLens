using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Source;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class RecommendedSourceRepository : IRecommendedSourceRepository
{
    private readonly TruthLensDbContext _db;

    public RecommendedSourceRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public Task<int> CountAsync(string? status, CancellationToken ct)
    {
        return BuildBaseQuery(status).CountAsync(ct);
    }

    public async Task<IReadOnlyList<RecommendedSource>> GetPageAsync(
        string? status,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var skip = (page - 1) * pageSize;

        return await BuildBaseQuery(status)
            .OrderByDescending(x => x.ConfidenceScore ?? -1)
            .ThenByDescending(x => x.LastSeenAtUtc)
            .ThenBy(x => x.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public Task<RecommendedSource?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _db.RecommendedSources.FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<IReadOnlyList<RecommendedSource>> GetForScoringAsync(int maxCount, CancellationToken ct)
    {
        return await _db.RecommendedSources
            .Where(x => x.Status == "pending" || x.Status == "approved")
            .OrderByDescending(x => x.LastSeenAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<bool> ExistsByFeedUrlAsync(string feedUrl, CancellationToken ct)
    {
        var normalized = feedUrl.Trim();
        return _db.RecommendedSources.AnyAsync(x => x.FeedUrl == normalized, ct);
    }

    public Task AddAsync(RecommendedSource source, CancellationToken ct) =>
        _db.RecommendedSources.AddAsync(source, ct).AsTask();

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    private IQueryable<RecommendedSource> BuildBaseQuery(string? status)
    {
        var query = _db.RecommendedSources.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim().ToLowerInvariant();
            query = query.Where(x => x.Status == normalized);
        }

        return query;
    }
}
