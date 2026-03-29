namespace TruthLens.Application.Repositories.Source;

using TruthLens.Domain.Entities;


public interface ISourceRepository
{
    Task<IReadOnlyList<Source>> GetActiveAsync(CancellationToken ct);
    Task<bool> ExistsByFeedUrlAsync(string feedUrl, CancellationToken ct);
    Task AddAsync(Source source, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
