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
}
