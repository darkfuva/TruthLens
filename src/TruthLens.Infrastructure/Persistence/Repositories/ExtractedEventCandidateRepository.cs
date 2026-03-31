using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.Event;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class ExtractedEventCandidateRepository : IExtractedEventCandidateRepository
{
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
}
