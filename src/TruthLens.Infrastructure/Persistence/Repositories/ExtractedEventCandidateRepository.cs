using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class ExtractedEventCandidateRepository : IExtractedEventCandidateRepository
{
    private static readonly string[] PendingStatuses = ["pending", "qualified", "weak"];
    private readonly TruthLensDbContext _db;

    public ExtractedEventCandidateRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public Task AddRangeAsync(IEnumerable<ExtractedEventCandidate> candidates, CancellationToken ct)
    {
        return _db.ExtractedEventCandidates.AddRangeAsync(candidates, ct);
    }

    public async Task<IReadOnlyList<ExtractedEventCandidate>> GetRecentForPostAsync(Guid postId, int maxCount, CancellationToken ct)
    {
        return await _db.ExtractedEventCandidates
            .Where(x => x.PostId == postId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ExtractedEventCandidate>> GetPendingBatchAsync(int batchSize, CancellationToken ct)
    {
        return await _db.ExtractedEventCandidates
            .Include(x => x.Post)
            .ThenInclude(p => p.EventLinks)
            .Where(x =>
                PendingStatuses.Contains(x.Status) &&
                x.Post.Embedding != null &&
                x.Post.EventLinks.Any(l => l.IsPrimary))
            .OrderByDescending(x => x.ExtractionConfidence)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(Math.Max(1, batchSize))
            .ToListAsync(ct);
    }
}
