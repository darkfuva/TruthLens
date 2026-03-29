using Microsoft.EntityFrameworkCore;
using TruthLens.Application.Repositories.External;
using TruthLens.Domain.Entities;

namespace TruthLens.Infrastructure.Persistence.Repositories;

public sealed class ExternalSourceRepository : IExternalSourceRepository
{
    private readonly TruthLensDbContext _db;

    public ExternalSourceRepository(TruthLensDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ExternalSource>> GetAllAsync(CancellationToken ct)
    {
        return await _db.ExternalSources.ToListAsync(ct);
    }

    public Task AddAsync(ExternalSource source, CancellationToken ct)
    {
        return _db.ExternalSources.AddAsync(source, ct).AsTask();
    }
}
