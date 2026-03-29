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
