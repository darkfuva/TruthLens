using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.External;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class ExternalEvidenceRepository : IExternalEvidenceRepository
{
    private readonly TruthLensDbContext _db;

    public ExternalEvidenceRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyCollection<string>> GetExistingUrlsForEventAsync(Guid eventId, CancellationToken ct)
    {
        return await _db.ExternalEvidencePosts
            .Where(x => x.EventId == eventId)
            .Select(x => x.Url)
            .ToListAsync(ct);
    }

    public Task AddRangeAsync(IEnumerable<ExternalEvidencePost> evidencePosts, CancellationToken ct)
    {
        return _db.ExternalEvidencePosts.AddRangeAsync(evidencePosts, ct);
    }
}
